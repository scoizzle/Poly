using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.Interpretation.Analysis.Semantics;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Abi;

/// <summary>
/// What a frame word means: one registered <see cref="LocalType"/> per CLR type.
/// <see cref="Of(Type)"/> is the one classification.
/// <see cref="ValueRepresentationClassifier.KindFor"/> reports <see cref="Kind"/>.
/// A typed read of a stored word is <see cref="FromWord"/>; a typed write is
/// <see cref="Encode"/>. A primitive of at most 64 bits is an
/// <see cref="Int64Local"/>, <see cref="Int32Local"/>, <see cref="IEEELocal"/>, or
/// <see cref="FlagLocal"/>, never a handle. <see cref="TemporalLocal"/> is a word
/// for <c>DateTime</c>, <c>DateOnly</c>, and <c>TimeOnly</c>.
/// </summary>
internal abstract record LocalType(Type Clr) {
    private static readonly ConcurrentDictionary<Type, LocalType> Forms = new();

    public abstract ValueRepresentationKind Kind { get; }

    /// <summary>The local type of <paramref name="clr"/>. Cached. Nullable
    /// wraps the local type of its underlying type.</summary>
    public static LocalType Of(Type clr) => Forms.GetOrAdd(clr, Create);

    /// <summary>Encode <paramref name="value"/> as the frame word of <paramref name="clr"/>.</summary>
    public static Expression Encode(Expression value, Type clr, Expression heap) {
        if (value.Type == typeof(long) && clr != typeof(long)
            && Nullable.GetUnderlyingType(clr) is null && clr != typeof(ulong))
            return value;
        var form = Of(clr);
        var nullableValue = Nullable.GetUnderlyingType(value.Type) is not null;
        if (value.Type != clr && value.Type != typeof(long) && !nullableValue
            && (form is NullableLocal || form.Kind == ValueRepresentationKind.StackScalar))
            value = Convert(value, clr);
        return form.ToFrame(value, heap);
    }

    /// <summary>Read a frame word whose meaning is <paramref name="source"/> as <paramref name="target"/>.</summary>
    public static Expression Decode(Expression word, Type? source, Type target, Expression heap) {
        source ??= word.Type;
        return Of(source).FromWord(word, target, heap);
    }

    /// <summary>The frame word for a runtime value. Null is the absent sentinel.</summary>
    public static long Write(object? value, VmManagedObjectHeap heap) =>
        value is null ? 0L : Of(value.GetType()).ToRing(value, heap);

    /// <summary>The local type of a node, after analysis replacement. A statement is
    /// none. A constant uses <see cref="Of(Type)"/> on the value's type.</summary>
    public static (LocalType? Form, Type? Clr) Of(INodeMetadataProvider? provider, Node? node) {
        if (node is null)
            return (null, null);
        var effective = provider?.GetNodeReplacement(node) ?? node;
        if (effective is Return { Value: { } returned })
            return Of(provider, returned);
        if (effective is Statement)
            return (NoneLocal.Instance, null);
        if (effective is TryCatchFinally tryCatch)
            return Of(provider, tryCatch.TryBlock);
        if (effective is Constant constant) {
            if (constant.Value is null)
                return (Of(typeof(object)), null);
            var valueType = constant.Value.GetType();
            return (Of(valueType), valueType);
        }
        var type = provider?.GetResolvedType(effective)?.GetRuntimeType()
            ?? provider?.GetResolvedMember(effective)?.MemberTypeDefinition.GetRuntimeType()
            ?? InferReturn(provider, effective);
        return type is null ? (null, null) : (Of(type), type);
    }

    private static Type? InferReturn(INodeMetadataProvider? provider, Node node) {
        if (provider is null)
            return null;
        if (node is not Invoke { Delegate: Member member } invoke)
            return null;
        var receiverDefinition = provider.GetResolvedType(member.Value);
        if (receiverDefinition is null or AstTypeDefinition)
            return null;
        var receiver = receiverDefinition.GetRuntimeType();
        if (receiver is null)
            return null;
        var returns = receiver
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(method => method.Name == member.MemberName
                && method.GetParameters().Length == invoke.Arguments.Length)
            .Select(method => method.ReturnType)
            .Distinct()
            .ToArray();
        return returns.Length == 1 ? returns[0] : null;
    }

    public static bool TryInline(object? value, out long raw) {
        if (value is null) {
            raw = 0L;
            return true;
        }
        var form = Of(value.GetType());
        if (form.Kind != ValueRepresentationKind.StackScalar) {
            raw = 0L;
            return false;
        }
        raw = form.ToRing(value, heap: null!);
        return true;
    }

    public abstract long ToRing(object value, VmManagedObjectHeap heap);

    /// <summary>The CLR type of a ring local for this form. <c>float</c>,
    /// <c>double</c>, the 32-bit integer family, and <c>Nullable&lt;T&gt;</c> keep
    /// a typed local. Every other form uses the <c>long</c> word.</summary>
    public Type SlotType => this switch {
        IEEELocal => Clr,
        Int32Local => typeof(int),
        NullableLocal => Clr,
        _ => typeof(long),
    };

    /// <summary>True when a ring local of <see cref="SlotType"/> holds the value
    /// rather than the word.</summary>
    public bool IsTyped => SlotType != typeof(long);

    public static bool IsInline(Type type) =>
        Of(type).Kind == ValueRepresentationKind.StackScalar;

    public static bool IsIeee(Type type) => Of(type) is IEEELocal;

    /// <summary>An expression-level value as its frame word. An <c>int</c> widens.
    /// IEEE becomes bits. Anything already a word stays a word.</summary>
    public static Expression ToWord(Expression value) =>
        value.Type == typeof(int) ? Convert(value, typeof(long))
        : Of(value.Type) is IEEELocal ? RingCodec.ToIeee(Convert(value, typeof(double)))
        : value;

    /// <summary>The word as a typed ring slot. Only <c>int</c>, <c>float</c>, and
    /// <c>double</c> slots differ from the word.</summary>
    public static Expression FromSlot(Expression word, Type slotType) =>
        slotType == typeof(int) ? Convert(word, typeof(int))
        : slotType == typeof(float) ? Convert(RingCodec.FromIeee(word), typeof(float))
        : slotType == typeof(double) ? RingCodec.FromIeee(word)
        : word;

    /// <summary><paramref name="value"/> stored at <paramref name="slotType"/>.
    /// Between a word and an IEEE slot this is the bit pattern. Between a word
    /// and an <c>int</c> slot it is numeric.</summary>
    public static Expression AsSlot(Expression value, Type slotType) {
        if (value.Type == slotType)
            return value;
        if (slotType == typeof(int))
            return Convert(Of(value.Type) is IEEELocal ? RingCodec.FromIeee(value) : value, typeof(int));
        if (value.Type == typeof(int))
            return slotType == typeof(double) ? Convert(value, typeof(double))
                : slotType == typeof(float) ? Convert(Convert(value, typeof(double)), typeof(float))
                : Convert(value, typeof(long));
        return FromSlot(ToWord(value), slotType);
    }

    /// <summary><paramref name="value"/> as the number <paramref name="target"/>
    /// denotes. A word widens to <c>double</c>. A <c>double</c> truncates. This
    /// is not the IEEE bitcast.</summary>
    public static Expression Numeric(Expression value, Type target) =>
        value.Type == target ? value
        : target == typeof(double) ? Convert(value, typeof(double))
        : target == typeof(float) ? Convert(Convert(value, typeof(double)), typeof(float))
        : target == typeof(int) ? Convert(value, typeof(int))
        : Convert(Convert(value, typeof(double)), typeof(long));

    public abstract Expression ToFrame(Expression value, Expression heap);

    public abstract Expression FromWord(Expression word, Type target, Expression heap);

    private static LocalType Create(Type clr) {
        var underlying = Nullable.GetUnderlyingType(clr);
        if (underlying is not null)
            return new NullableLocal(clr, Of(underlying));
        if (clr == typeof(void))
            return NoneLocal.Instance;
        if (clr == typeof(bool))
            return new FlagLocal(clr);
        if (clr == typeof(float) || clr == typeof(double))
            return new IEEELocal(clr);
        if (IsInt32Family(clr))
            return new Int32Local(clr);
        if (clr.IsPrimitive || clr.IsEnum)
            return new Int64Local(clr);
        if (!clr.IsValueType)
            return new HandleLocal(clr);
        if (clr == typeof(DateTime) || clr == typeof(DateOnly)
            || clr == typeof(TimeOnly) || clr == typeof(DateTimeOffset))
            return new TemporalLocal(clr);
        return new StructLocal(clr);
    }

    /// <summary>Types whose arithmetic is a 32-bit operation, plus an enum with
    /// one of those underlying types. <c>uint</c> is not one of them: mixed with
    /// <c>int</c> it promotes to <c>long</c>.</summary>
    private static bool IsInt32Family(Type clr) {
        var underlying = clr.IsEnum ? Enum.GetUnderlyingType(clr) : clr;
        return underlying == typeof(int) || underlying == typeof(short)
            || underlying == typeof(ushort) || underlying == typeof(byte)
            || underlying == typeof(sbyte) || underlying == typeof(char);
    }

    /// <summary>A scalar word brought to <paramref name="target"/>.</summary>
    protected Expression Finish(Expression value, Type target) {
        if (value.Type == target)
            return value;
        if (target == typeof(bool))
            return NotEqual(RingCodec.ToWord(value), Constant(0L));
        return Convert(value, target);
    }

    /// <summary>An inline integer wider than 32 bits, a <c>uint</c>, an enum with
    /// a 64-bit underlying type, <c>nint</c>, or <c>nuint</c>. The frame holds the
    /// number. <c>ulong</c> and <c>nuint</c> are bitcasts. The 32-bit family is
    /// <see cref="Int32Local"/>.</summary>
    internal sealed record Int64Local(Type Clr) : LocalType(Clr) {
        private static readonly MethodInfo UIntPtrToUInt64 =
            typeof(nuint).GetMethod(nameof(nuint.ToUInt64), Type.EmptyTypes)!;
        private static readonly ConstructorInfo UIntPtrFromULong =
            typeof(nuint).GetConstructor([typeof(ulong)])!;

        public override ValueRepresentationKind Kind => ValueRepresentationKind.StackScalar;

        public override Expression ToFrame(Expression value, Expression heap) {
            if (Clr == typeof(ulong) || value.Type == typeof(ulong))
                return value.Type == typeof(long) ? value : Convert(value, typeof(long));
            if (Clr == typeof(nuint) || value.Type == typeof(nuint))
                return value.Type == typeof(long)
                    ? value
                    : Convert(Call(value, UIntPtrToUInt64), typeof(long));
            if (value.Type == typeof(long))
                return value;
            if (value.Type != Clr && (Clr.IsPrimitive || Clr == typeof(char) || Clr.IsEnum))
                value = Convert(value, Clr);
            return Convert(value, typeof(long));
        }

        public override Expression FromWord(Expression word, Type target, Expression heap) =>
            Finish(Materialize(word), target);

        public override long ToRing(object value, VmManagedObjectHeap heap) => value switch {
            byte b => b,
            sbyte sb => sb,
            short s => s,
            ushort us => us,
            int i => i,
            uint ui => ui,
            long l => l,
            ulong ul => unchecked((long)ul),
            char c => c,
            nint p => p,
            nuint p => unchecked((long)p),
            Enum e => EnumToWord(e),
            _ => throw new InvalidOperationException(
                $"VM compile rejected: '{value.GetType()}' is not an inline word."),
        };

        private static long EnumToWord(Enum value) {
            var under = System.Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()));
            return under is ulong ul ? unchecked((long)ul) : System.Convert.ToInt64(under);
        }

        private Expression Materialize(Expression word) {
            if (word.Type == Clr)
                return word;
            if (Clr == typeof(ulong))
                return Convert(RingCodec.ToWord(word), typeof(ulong));
            if (Clr == typeof(nuint))
                return New(UIntPtrFromULong, Convert(RingCodec.ToWord(word), typeof(ulong)));
            return Convert(RingCodec.ToWord(word), Clr);
        }
    }

    /// <summary>The 32-bit integer family: <c>int</c> and every integral type C#
    /// promotes to <c>int</c>, plus an enum with such an underlying type. The local
    /// is <c>int</c>. The frame holds the word. A store into a smaller member of
    /// the family narrows with the unchecked cast.</summary>
    internal sealed record Int32Local(Type Clr) : LocalType(Clr) {
        private static readonly MethodInfo UIntPtrToUInt64 =
            typeof(nuint).GetMethod(nameof(nuint.ToUInt64), Type.EmptyTypes)!;

        public override ValueRepresentationKind Kind => ValueRepresentationKind.StackScalar;

        public override Expression ToFrame(Expression value, Expression heap) {
            if (value.Type == typeof(ulong))
                return Convert(value, typeof(long));
            if (value.Type == typeof(nuint))
                return Convert(Call(value, UIntPtrToUInt64), typeof(long));
            if (value.Type == typeof(long))
                return value;
            if (value.Type != Clr)
                value = Convert(value, Clr);
            return Convert(value, typeof(long));
        }

        public override Expression FromWord(Expression word, Type target, Expression heap) =>
            Finish(word.Type == Clr ? word : Convert(RingCodec.ToWord(word), Clr), target);

        public override long ToRing(object value, VmManagedObjectHeap heap) => value switch {
            byte b => b,
            sbyte sb => sb,
            short s => s,
            ushort us => us,
            int i => i,
            uint ui => ui,
            long l => l,
            ulong ul => unchecked((long)ul),
            char c => c,
            nint p => p,
            nuint p => unchecked((long)p),
            Enum e => EnumToWord(e),
            _ => throw new InvalidOperationException(
                $"VM compile rejected: '{value.GetType()}' is not an inline word."),
        };

        private static long EnumToWord(Enum value) {
            var under = System.Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()));
            return under is ulong ul ? unchecked((long)ul) : System.Convert.ToInt64(under);
        }
    }

    /// <summary><c>float</c> or <c>double</c>. The frame holds the IEEE bits.
    /// Both widths use a <c>double</c>'s bit pattern.</summary>
    internal sealed record IEEELocal(Type Clr) : LocalType(Clr) {
        public override ValueRepresentationKind Kind => ValueRepresentationKind.StackScalar;

        public override Expression ToFrame(Expression value, Expression heap) =>
            RingCodec.ToIeee(Convert(value, typeof(double)));

        public override long ToRing(object value, VmManagedObjectHeap heap) => value switch {
            float f => BitConverter.DoubleToInt64Bits(f),
            double d => BitConverter.DoubleToInt64Bits(d),
            _ => throw new InvalidOperationException(
                $"VM compile rejected: '{value.GetType()}' is not an IEEE value."),
        };

        public override Expression FromWord(Expression word, Type target, Expression heap) {
            Expression value = word.Type == Clr
                ? word
                : RingCodec.FromIeee(RingCodec.ToWord(word));
            if (Clr == typeof(float) && value.Type != typeof(float))
                value = Convert(value, typeof(float));
            return Finish(value, target);
        }
    }

    /// <summary><c>bool</c>. The frame holds 0 or 1.</summary>
    internal sealed record FlagLocal(Type Clr) : LocalType(Clr) {
        public override ValueRepresentationKind Kind => ValueRepresentationKind.StackScalar;

        public override Expression ToFrame(Expression value, Expression heap) =>
            value.Type == typeof(bool)
                ? Condition(value, Constant(1L), Constant(0L))
                : value;

        public override long ToRing(object value, VmManagedObjectHeap heap) =>
            value is true ? 1L : 0L;

        public override Expression FromWord(Expression word, Type target, Expression heap) =>
            Finish(word.Type == typeof(bool) ? word : NotEqual(RingCodec.ToWord(word), Constant(0L)), target);
    }

    /// <summary>A heap handle: a reference type, or a value that does not fit in
    /// 64 bits. <see cref="TemporalLocal"/> and <see cref="StructLocal"/> are the
    /// value-type registrations and share this encoding.</summary>
    internal record HandleLocal(Type Clr) : LocalType(Clr) {
        public override ValueRepresentationKind Kind => ValueRepresentationKind.HeapRef;

        public override Expression ToFrame(Expression value, Expression heap) =>
            RingCodec.Box(Convert(value, typeof(object)), heap);

        public override long ToRing(object value, VmManagedObjectHeap heap) =>
            heap.Allocate(value);

        public override Expression FromWord(Expression word, Type target, Expression heap) {
            if (Nullable.GetUnderlyingType(target) is not null)
                return RingCodec.UnboxToNullable(word, target, heap);
            if (target == typeof(long))
                return RingCodec.ToWord(word);
            return Convert(RingCodec.DerefObject(heap, word), target);
        }
    }

    /// <summary>A temporal value. <c>DateTime</c> (<see cref="DateTime.ToBinary"/>,
    /// which keeps <see cref="DateTimeKind"/>), <c>DateOnly</c> (<see cref="DateOnly.DayNumber"/>),
    /// and <c>TimeOnly</c> (<see cref="TimeOnly.Ticks"/>) are one word.
    /// <c>DateTimeOffset</c> is 16 bytes and stays a heap handle.</summary>
    internal sealed record TemporalLocal(Type Clr) : HandleLocal(Clr) {
        private static readonly MethodInfo DateTimeToBinary =
            typeof(DateTime).GetMethod(nameof(DateTime.ToBinary), Type.EmptyTypes)!;
        private static readonly MethodInfo DateTimeFromBinary =
            typeof(DateTime).GetMethod(nameof(DateTime.FromBinary), [typeof(long)])!;
        private static readonly MethodInfo DateOnlyFromDayNumber =
            typeof(DateOnly).GetMethod(nameof(DateOnly.FromDayNumber), [typeof(int)])!;
        private static readonly ConstructorInfo TimeOnlyFromTicks =
            typeof(TimeOnly).GetConstructor([typeof(long)])!;

        /// <summary>True when the value is a single frame word, not a handle.</summary>
        public bool IsWord => Clr != typeof(DateTimeOffset);

        public override ValueRepresentationKind Kind =>
            IsWord ? ValueRepresentationKind.StackScalar : ValueRepresentationKind.HeapRef;

        public override Expression ToFrame(Expression value, Expression heap) {
            if (!IsWord)
                return base.ToFrame(value, heap);
            if (value.Type == typeof(long))
                return value;
            if (value.Type != Clr)
                value = Convert(value, Clr);
            if (Clr == typeof(DateTime))
                return Call(value, DateTimeToBinary);
            if (Clr == typeof(DateOnly))
                return Convert(Property(value, nameof(DateOnly.DayNumber)), typeof(long));
            return Property(value, nameof(TimeOnly.Ticks));
        }

        public override long ToRing(object value, VmManagedObjectHeap heap) {
            if (!IsWord)
                return base.ToRing(value, heap);
            return value switch {
                DateTime dateTime => dateTime.ToBinary(),
                DateOnly date => date.DayNumber,
                TimeOnly time => time.Ticks,
                _ => throw new InvalidOperationException(
                    $"VM compile rejected: '{value.GetType()}' is not a {Clr.Name}."),
            };
        }

        public override Expression FromWord(Expression word, Type target, Expression heap) {
            if (!IsWord)
                return base.FromWord(word, target, heap);
            if (word.Type == Clr)
                return Finish(word, target);
            var bits = RingCodec.ToWord(word);
            Expression value = Clr == typeof(DateTime)
                ? Call(DateTimeFromBinary, bits)
                : Clr == typeof(DateOnly)
                    ? Call(DateOnlyFromDayNumber, Convert(bits, typeof(int)))
                    : New(TimeOnlyFromTicks, bits);
            return Finish(value, target);
        }

        /// <summary>The CLR value of a frame word. A zero word is the default value.</summary>
        public object Read(long word) =>
            Clr == typeof(DateTime) ? DateTime.FromBinary(word)
            : Clr == typeof(DateOnly) ? DateOnly.FromDayNumber(checked((int)word))
            : new TimeOnly(word);
    }

    /// <summary>A value type that does not fit one word: <c>decimal</c>,
    /// <c>Guid</c>, <c>Int128</c>, <c>UInt128</c>, or any other non-temporal
    /// struct. The frame holds a heap handle.</summary>
    internal sealed record StructLocal(Type Clr) : HandleLocal(Clr);

    /// <summary>A <c>Nullable&lt;T&gt;</c> whose underlying value is <see cref="Inner"/>.
    /// The frame holds a handle, and 0 is absent.</summary>
    internal sealed record NullableLocal(Type Clr, LocalType Inner) : LocalType(Clr) {
        public override ValueRepresentationKind Kind => ValueRepresentationKind.HeapRef;

        public override Expression ToFrame(Expression value, Expression heap) {
            if (value.Type == typeof(long))
                return value;
            if (value.Type != Clr)
                value = Convert(value, Clr);
            return RingCodec.BoxNullable(value, heap);
        }

        public override long ToRing(object value, VmManagedObjectHeap heap) =>
            heap.Allocate(value);

        public override Expression FromWord(Expression word, Type target, Expression heap) {
            if (word.Type != Clr) {
                var present = RingCodec.UnboxToNullable(word, Clr, heap);
                return target == Clr ? present : Convert(present, target);
            }
            if (Nullable.GetUnderlyingType(target) is not null)
                return RingCodec.UnboxToNullable(word, target, heap);
            if (target == typeof(long))
                return RingCodec.ToWord(word);
            return Convert(RingCodec.DerefObject(heap, word), target);
        }
    }

    /// <summary><c>void</c> has no frame word.</summary>
    internal sealed record NoneLocal : LocalType {
        public static readonly NoneLocal Instance = new();

        private NoneLocal() : base(typeof(void)) { }

        public override ValueRepresentationKind Kind => ValueRepresentationKind.Void;

        public override long ToRing(object value, VmManagedObjectHeap heap) =>
            throw new InvalidOperationException("VM compile rejected: void has no frame word.");

        public override Expression ToFrame(Expression value, Expression heap) =>
            throw new InvalidOperationException("VM compile rejected: void has no frame word.");

        public override Expression FromWord(Expression word, Type target, Expression heap) =>
            throw new InvalidOperationException("VM compile rejected: void has no frame word.");
    }
}
