using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.Interpretation.Analysis.Semantics;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Abi;

/// <summary>
/// What a frame word means. <see cref="For"/> is the one classification.
/// <see cref="ValueRepresentationClassifier.KindFor"/> reports <see cref="Kind"/>.
/// <see cref="SlotLocation.View"/> reads and writes through <see cref="Encode"/>
/// and <see cref="FromWord"/>. A primitive of at most 64 bits is a
/// <see cref="Word"/>, <see cref="Bits"/>, or <see cref="Flag"/>, never a handle.
/// </summary>
internal abstract record FrameRepresentation(Type Clr) {
    private static readonly ConcurrentDictionary<Type, FrameRepresentation> Forms = new();

    public abstract ValueRepresentationKind Kind { get; }

    /// <summary>The representation of <paramref name="clr"/>. Cached. Nullable
    /// wraps the representation of its underlying type.</summary>
    public static FrameRepresentation For(Type clr) => Forms.GetOrAdd(clr, Create);

    /// <summary>Encode <paramref name="value"/> as the frame word of <paramref name="clr"/>.</summary>
    public static Expression Encode(Expression value, Type clr, Expression heap) {
        if (value.Type == typeof(long) && clr != typeof(long)
            && Nullable.GetUnderlyingType(clr) is null && clr != typeof(ulong))
            return value;
        var form = For(clr);
        var nullableValue = Nullable.GetUnderlyingType(value.Type) is not null;
        if (value.Type != clr && value.Type != typeof(long) && !nullableValue
            && (form is Optional || form.Kind == ValueRepresentationKind.StackScalar))
            value = Convert(value, clr);
        return form.ToFrame(value, heap);
    }

    /// <summary>Read a frame word whose meaning is <paramref name="source"/> as <paramref name="target"/>.</summary>
    public static Expression Decode(Expression word, Type? source, Type target, Expression heap) {
        source ??= word.Type;
        return For(source).FromWord(word, target, heap);
    }

    /// <summary>The frame word for a runtime value. Null is the absent sentinel.</summary>
    public static long Write(object? value, VmManagedObjectHeap heap) =>
        value is null ? 0L : For(value.GetType()).ToRing(value, heap);

    /// <summary>The form of a node, after analysis replacement. A statement is
    /// none. A constant uses <see cref="For"/> on the value's type.</summary>
    public static (FrameRepresentation? Form, Type? Clr) Of(INodeMetadataProvider? provider, Node? node) {
        if (node is null)
            return (null, null);
        var effective = provider?.GetNodeReplacement(node) ?? node;
        if (effective is Return { Value: { } returned })
            return Of(provider, returned);
        if (effective is Statement)
            return (None.Instance, null);
        if (effective is TryCatchFinally tryCatch)
            return Of(provider, tryCatch.TryBlock);
        if (effective is Constant constant) {
            if (constant.Value is null)
                return (For(typeof(object)), null);
            var valueType = constant.Value.GetType();
            return (For(valueType), valueType);
        }
        var type = provider?.GetResolvedType(effective)?.GetRuntimeType()
            ?? provider?.GetResolvedMember(effective)?.MemberTypeDefinition.GetRuntimeType()
            ?? InferReturn(provider, effective);
        return type is null ? (null, null) : (For(type), type);
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
        var form = For(value.GetType());
        if (form.Kind != ValueRepresentationKind.StackScalar) {
            raw = 0L;
            return false;
        }
        raw = form.ToRing(value, heap: null!);
        return true;
    }

    public abstract long ToRing(object value, VmManagedObjectHeap heap);

    /// <summary>The CLR type of a ring local for this form. <c>float</c>,
    /// <c>double</c>, <c>int</c>, and <c>Nullable&lt;T&gt;</c> keep their type.
    /// Every other form uses the <c>long</c> word.</summary>
    public Type SlotType => this switch {
        Bits => Clr,
        Word word when word.Clr == typeof(int) => typeof(int),
        Optional => Clr,
        _ => typeof(long),
    };

    /// <summary>True when a ring local of <see cref="SlotType"/> holds the value
    /// rather than the word.</summary>
    public bool IsTyped => SlotType != typeof(long);

    public static bool IsInline(Type type) =>
        For(type).Kind == ValueRepresentationKind.StackScalar;

    public static bool IsIeee(Type type) => For(type) is Bits;

    /// <summary>An expression-level value as its frame word. An <c>int</c> widens.
    /// IEEE becomes bits. Anything already a word stays a word.</summary>
    public static Expression ToWord(Expression value) =>
        value.Type == typeof(int) ? Convert(value, typeof(long))
        : For(value.Type) is Bits ? RingCodec.ToIeee(Convert(value, typeof(double)))
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
            return Convert(For(value.Type) is Bits ? RingCodec.FromIeee(value) : value, typeof(int));
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

    private static FrameRepresentation Create(Type clr) {
        var underlying = Nullable.GetUnderlyingType(clr);
        if (underlying is not null)
            return new Optional(clr, For(underlying));
        if (clr == typeof(void))
            return None.Instance;
        if (clr == typeof(bool))
            return new Flag(clr);
        if (clr == typeof(float) || clr == typeof(double))
            return new Bits(clr);
        if (clr.IsPrimitive || clr.IsEnum)
            return new Word(clr);
        return new Handle(clr);
    }

    /// <summary>A scalar word brought to <paramref name="target"/>.</summary>
    protected Expression Finish(Expression value, Type target) {
        if (value.Type == target)
            return value;
        if (target == typeof(bool))
            return NotEqual(RingCodec.ToWord(value), Constant(0L));
        return Convert(value, target);
    }

    /// <summary>An inline integer, <c>char</c>, enum, <c>nint</c>, or <c>nuint</c>.
    /// The frame holds the number. <c>ulong</c> and <c>nuint</c> are bitcasts.</summary>
    internal sealed record Word(Type Clr) : FrameRepresentation(Clr) {
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

    /// <summary><c>float</c> or <c>double</c>. The frame holds the IEEE bits.
    /// Both widths use a <c>double</c>'s bit pattern.</summary>
    internal sealed record Bits(Type Clr) : FrameRepresentation(Clr) {
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
    internal sealed record Flag(Type Clr) : FrameRepresentation(Clr) {
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

    /// <summary>A heap handle: <c>decimal</c>, a reference, or any other value
    /// that does not fit in 64 bits.</summary>
    internal sealed record Handle(Type Clr) : FrameRepresentation(Clr) {
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

    /// <summary>A <c>Nullable&lt;T&gt;</c> whose underlying value is <see cref="Inner"/>.
    /// The frame holds a handle, and 0 is absent.</summary>
    internal sealed record Optional(Type Clr, FrameRepresentation Inner) : FrameRepresentation(Clr) {
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
    internal sealed record None : FrameRepresentation {
        public static readonly None Instance = new();

        private None() : base(typeof(void)) { }

        public override ValueRepresentationKind Kind => ValueRepresentationKind.Void;

        public override long ToRing(object value, VmManagedObjectHeap heap) =>
            throw new InvalidOperationException("VM compile rejected: void has no frame word.");

        public override Expression ToFrame(Expression value, Expression heap) =>
            throw new InvalidOperationException("VM compile rejected: void has no frame word.");

        public override Expression FromWord(Expression word, Type target, Expression heap) =>
            throw new InvalidOperationException("VM compile rejected: void has no frame word.");
    }
}