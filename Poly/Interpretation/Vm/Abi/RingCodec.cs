using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Abi;

/// <summary>
/// The single definition of the VM's long-slot ABI: how a value is classified
/// (inline word — numeric, boolean 0/non-zero, or IEEE bit pattern — and heap
/// handle) and converted in both directions — at compile time (<see cref="Expression"/> factories) and at
/// runtime (<see cref="ToRing(object?, VmManagedObjectHeap)"/> and friends).
/// </summary>
/// <remarks>
/// Every ABI site delegates here. Analysis passes use <see cref="IsInline(Type)"/>
/// to predict the same classification the emitter applies. Handle 0 is the ABI
/// null sentinel; it is never a live object.
/// </remarks>
internal static class RingCodec {
    private static readonly MethodInfo HeapUnsafeGet = Ref<VmManagedObjectHeap>.Method(h => h.UnsafeGet(0));
    private static readonly MethodInfo HeapAllocate = Ref<VmManagedObjectHeap>.Method(h => h.Allocate(default!));
    private static readonly MethodInfo BoxToAbiInfo =
        Ref.Method((Expression<Func<VmManagedObjectHeap, object?, long>>)((h, v) => BoxToAbi(h, v)));
    private static readonly MethodInfo BitConverterInt64BitsToDouble =
        Ref.Method((Expression<Func<long, double>>)(v => BitConverter.Int64BitsToDouble(v)));
    private static readonly MethodInfo BitConverterDoubleToInt64Bits =
        Ref.Method((Expression<Func<double, long>>)(v => BitConverter.DoubleToInt64Bits(v)));

    // ── Classification ───────────────────────────────────────────

    /// <summary>The ring-slot shape of a type's <em>underlying</em> word: a
    /// nullable is peeled, enums are inline scalars, and float/double are inline
    /// IEEE bit patterns (classified <see cref="ValueRepresentationKind.StackScalar"/>
    /// because they occupy one slot — <see cref="IsIeee(Type)"/> carries the
    /// bit-pattern distinction).
    /// <para>This answers "is the underlying value long-representable", which is
    /// what boxing, array element shapes, and unboxing ask. It is <b>not</b> the
    /// slot kind of a nullable <em>node</em>: a nullable is boxed into a heap
    /// handle, which is <see cref="ValueRepresentationClassifier.KindFor(Type)"/>'s
    /// rule. Keep the two questions apart — same signature, different meaning.</para></summary>
    public static ValueRepresentationKind UnderlyingKindFor(Type type) {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t.IsEnum) return ValueRepresentationKind.StackScalar;
        // A boolean is an inline word (0 = false, non-zero = true); its CLR type is
        // what marks it boolean at decode boundaries.
        if (t == typeof(bool)) return ValueRepresentationKind.StackScalar;
        if (t == typeof(long) || t == typeof(int) || t == typeof(short)
            || t == typeof(byte) || t == typeof(sbyte) || t == typeof(ushort)
            || t == typeof(uint) || t == typeof(ulong) || t == typeof(char)
            || t == typeof(float) || t == typeof(double))
            return ValueRepresentationKind.StackScalar;
        return ValueRepresentationKind.HeapRef;
    }

    /// <summary>True when the type's <em>underlying</em> value is an inline word
    /// rather than a heap value. A nullable returns true: its underlying value is
    /// a word, even though a nullable node's slot holds a handle. Callers that
    /// mean "this slot holds the word" must ask
    /// <see cref="ValueRepresentationClassifier.KindFor(Type)"/> instead.</summary>
    public static bool IsInline(Type type) =>
        UnderlyingKindFor(type) is ValueRepresentationKind.StackScalar;

    /// <summary>True when the type stores its IEEE bit pattern in the slot.
    /// Exact <c>float</c>/<c>double</c> only; a nullable wraps the value in a
    /// heap cell, matching <see cref="FromClr"/>.</summary>
    public static bool IsIeee(Type type) =>
        type == typeof(float) || type == typeof(double);

    /// <summary>True for the types C# performs an arithmetic operator on at 32 bits:
    /// <c>int</c>, plus every smaller integral type, which C# promotes to <c>int</c>
    /// before the operator runs (<c>short + short</c> is an <c>int</c> addition).
    /// <c>uint</c> is excluded: it is 32-bit but does not fit the signed <c>int</c>
    /// representation, so it keeps the word.</summary>
    public static bool IsInt32Width(Type type) =>
        type == typeof(int) || type == typeof(short) || type == typeof(byte)
        || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(char);

    /// <summary>True for the types C# performs an arithmetic operator on at 64 bits.</summary>
    public static bool IsInt64Width(Type type) =>
        type == typeof(long) || type == typeof(ulong);

    /// <summary>The width an integer arithmetic operation runs at, from the two operand
    /// types analysis resolved. C# promotes to the wider operand, so one <c>long</c> makes
    /// the operation 64-bit; two 32-bit-width operands make it <c>int</c>, and the CLR then
    /// wraps the result at 32 bits.
    /// <para>An operand whose width analysis could not name has no width: this throws
    /// rather than answering the word, because a silent 64-bit guess is how
    /// [#289](https://github.com/scoizzle/Poly/issues/289) arose. A resolved non-integer
    /// type (a <c>bool</c>, a <c>decimal</c>) keeps the word it has always had.</para></summary>
    public static Type IntegerArithmeticWidth(Type? left, Type? right) {
        if (left is null || right is null)
            throw new InvalidOperationException(
                "VM compile rejected: cannot resolve the integer width of an arithmetic "
                + $"operand (left={(left?.Name ?? "unresolved")}, right={(right?.Name ?? "unresolved")}).");
        if (IsInt64Width(left) || IsInt64Width(right))
            return typeof(long);
        return IsInt32Width(left) && IsInt32Width(right) ? typeof(int) : typeof(long);
    }

    /// <summary>An IEEE <c>double</c> expression as its ring slot: the raw bit
    /// pattern. The one writer of the double-to-long bitcast.</summary>
    public static Expression ToIeee(Expression ieeeDouble) =>
        Call(BitConverterDoubleToInt64Bits, ieeeDouble);

    /// <summary>A ring slot holding IEEE bits, as a CLR <c>double</c>. The one
    /// reader of the long-to-double bitcast.</summary>
    public static Expression FromIeee(Expression ringVal) =>
        Call(BitConverterInt64BitsToDouble, ringVal);

    /// <summary>The CLR type of a ring local holding a value of
    /// <paramref name="clrType"/>: <c>double</c>, <c>float</c> and <c>int</c> keep their
    /// own type, and every other shape — a wider integer, a smaller integral type C#
    /// promotes to <c>int</c> before an operator, a handle, or a type analysis could not
    /// resolve — uses the <c>long</c> word.</summary>
    public static Type SlotTypeOf(Type? clrType) =>
        clrType == typeof(float) || clrType == typeof(double) || clrType == typeof(int)
            ? clrType
            : typeof(long);

    /// <summary>True when a ring local's own type is the value, rather than the word.</summary>
    public static bool IsTypedSlot(Type slotType) =>
        IsIeee(slotType) || slotType == typeof(int);

    /// <summary>A typed slot expression as the word. The one place an expression-level
    /// IEEE value becomes its bit pattern; a <c>float</c> widens first because the word
    /// form of both IEEE widths is a <c>double</c>'s bits, and an <c>int</c> widens
    /// numerically because it is a number, not a bit pattern.</summary>
    public static Expression ToWord(Expression value) =>
        value.Type == typeof(int) ? Convert(value, typeof(long))
            : IsIeee(value.Type) ? ToIeee(Convert(value, typeof(double)))
            : value;

    /// <summary>The word as a typed slot expression: the mirror of
    /// <see cref="ToWord"/>, for a local whose own type is <paramref name="slotType"/>.</summary>
    public static Expression FromWord(Expression word, Type slotType) =>
        slotType == typeof(int) ? Convert(word, typeof(int))
            : slotType == typeof(float) ? Convert(FromIeee(word), typeof(float))
            : slotType == typeof(double) ? FromIeee(word)
            : word;

    /// <summary><paramref name="value"/> at <paramref name="representation"/>: the value
    /// itself when it is already there, otherwise one conversion. Between the word and an
    /// IEEE slot that conversion is the bit pattern (they are two spellings of one storage);
    /// between the word and an <c>int</c> slot it is numeric, because an <c>int</c> slot
    /// holds the number. This is what a storage boundary asks for, so no access converts
    /// more than once.</summary>
    public static Expression AsRepresentation(Expression value, Type representation) {
        if (value.Type == representation)
            return value;
        if (representation == typeof(int))
            return Convert(IsIeee(value.Type) ? FromIeee(value) : value, typeof(int));
        if (value.Type == typeof(int))
            return representation == typeof(double) ? Convert(value, typeof(double))
                : representation == typeof(float) ? Convert(Convert(value, typeof(double)), typeof(float))
                : Convert(value, typeof(long));
        return FromWord(ToWord(value), representation);
    }

    /// <summary><paramref name="value"/> at <paramref name="representation"/> as the
    /// <em>number</em> it denotes, not its bit pattern: a word widens to a double, a
    /// double truncates to the word. A typed storage means the destination's own type
    /// decides the conversion, so it is the numeric one.</summary>
    public static Expression NumericTo(Expression value, Type representation) =>
        value.Type == representation ? value
            : representation == typeof(double) ? Convert(value, typeof(double))
            : representation == typeof(float) ? Convert(Convert(value, typeof(double)), typeof(float))
            : representation == typeof(int) ? Convert(value, typeof(int))
            : Convert(Convert(value, typeof(double)), typeof(long));

    /// <summary>Fold a compile-time constant into its inline ring slot. Null is
    /// the null sentinel 0; float and double are IEEE bit patterns; <c>ulong</c>
    /// uses an unchecked 64-bit bitcast so <c>ulong.MaxValue</c> round-trips.</summary>
    public static bool TryInlineConstant(object? value, out long raw) {
        switch (value) {
            case null: raw = 0; return true;
            case bool b: raw = b ? 1 : 0; return true;
            case byte b: raw = b; return true;
            case sbyte sb: raw = sb; return true;
            case short s: raw = s; return true;
            case ushort us: raw = us; return true;
            case int i: raw = i; return true;
            case uint ui: raw = ui; return true;
            case long l: raw = l; return true;
            case ulong ul: raw = unchecked((long)ul); return true;
            case char c: raw = c; return true;
            case float f: raw = BitConverter.DoubleToInt64Bits(f); return true;
            case double d: raw = BitConverter.DoubleToInt64Bits(d); return true;
            case Enum e: raw = EnumWord(e); return true;
            default: raw = 0; return false;
        }
    }

    /// <summary>The ring word of an enum: its underlying integer, sign-extended
    /// into <c>long</c>. A <c>ulong</c> underlying uses the same unchecked bitcast
    /// as a <c>ulong</c> scalar, so <c>ulong.MaxValue</c> round-trips.</summary>
    public static long EnumWord(Enum value) {
        var underlying = Enum.GetUnderlyingType(value.GetType());
        return underlying == typeof(ulong)
            ? unchecked((long)System.Convert.ToUInt64(value))
            : System.Convert.ToInt64(value);
    }

    /// <summary>True when a node's value is an IEEE bit pattern (float/double).</summary>
    public static bool IsIeee(Node node, AnalysisResult? analysis) =>
        ValueRepresentationClassifier.ClrTypeOf(analysis, node) is { } clr && IsIeee(clr);

    /// <summary>True when a node's slot uses the ABI null sentinel: a literal
    /// null, a heap reference, or a nullable value type.</summary>
    public static bool UsesNullSentinel(Node node, AnalysisResult? analysis) {
        var (kind, clr) = ValueRepresentationClassifier.Classify(analysis, node);
        if (kind == ValueRepresentationKind.HeapRef) return true;
        return clr is Type t && Nullable.GetUnderlyingType(t) is not null;
    }

    /// <summary>Whether a call argument sits in its slot as a raw scalar (not a
    /// heap handle). The value representation decides; without one, the
    /// argument's resolved type does. An argument the emitter cannot place is a
    /// compile error — there is no "treat unknown as a handle" fallback.</summary>
    public static bool IsStackScalar(Node node, AnalysisResult? analysis) {
        switch (ValueRepresentationClassifier.KindOf(analysis, node)) {
            case ValueRepresentationKind.StackScalar:
                return true;
            case ValueRepresentationKind.HeapRef:
                return false;
        }
        var type = analysis?.GetResolvedType(node)
            ?? throw new InvalidOperationException(
                $"VM compile rejected: cannot tell whether argument '{node}' is a scalar or a reference.");
        return type.GetRuntimeType() is { IsValueType: true };
    }

    // ── Compile-time conversion ──────────────────────────────────

    /// <summary>Convert a CLR-typed expression into its ring slot: IEEE bitcast
    /// for float/double, widening for inline scalars, heap handle otherwise.</summary>
    public static Expression FromClr(Expression value, Type? clrType, Expression heap) {
        if (clrType == typeof(float) || clrType == typeof(double))
            return ToIeee(Convert(value, typeof(double)));
        // A nullable is heap-resident: absent is the sentinel 0, present is a handle
        // to the boxed underlying value. A value read from a nullable member or a
        // nullable call return must not collapse to the underlying word.
        if (clrType is not null && Nullable.GetUnderlyingType(clrType) is not null)
            return BoxNullable(value, heap);
        // An enum's slot is its underlying integer, the same word ToRing and the
        // constant encoder write. A ulong underlying converts to long as a bitcast.
        if (clrType is { IsEnum: true })
            return Convert(Convert(value, Enum.GetUnderlyingType(clrType)), typeof(long));
        if (clrType is not null && clrType.IsValueType && IsInline(clrType))
            return Convert(Convert(value, clrType), typeof(long));
        return Call(BoxToAbiInfo, heap, Convert(value, typeof(object)));
    }

    /// <summary>Box a nullable value as its slot: the null sentinel when it has no
    /// value, otherwise a handle to the boxed underlying value.</summary>
    private static Expression BoxNullable(Expression value, Expression heap) {
        var boxed = Variable(typeof(object), "_nullable");
        return Block([boxed],
            Assign(boxed, Convert(value, typeof(object))),
            Condition(
                Equal(boxed, Constant(null, typeof(object))),
                Constant(0L),
                Convert(Call(heap, HeapAllocate, boxed), typeof(long))));
    }

    /// <summary>Read a ring slot as a CLR value of <paramref name="targetType"/>.
    /// An inline target converts the slot in place; a scalar reaching <c>object</c>
    /// is boxed by its source type; everything else is dereferenced as a handle.</summary>
    public static Expression ToClr(
        Expression ringVal, Type targetType, Node? source, AnalysisResult? analysis, Expression heap) {
        var (sourceKind, sourceClr) = ValueRepresentationClassifier.Classify(analysis, source);
        if (targetType == typeof(double) || targetType == typeof(float)) {
            if (sourceClr is not null && IsIeee(sourceClr))
                return Convert(FromIeee(ringVal), targetType);
            return Convert(ringVal, targetType);
        }
        if (targetType == typeof(bool))
            return NotEqual(ringVal, Constant(0L));
        // A constant the encoder does not inline (a decimal, a string) is a handle.
        if (source is Constant constant && constant.Value is not null
            && !TryInlineConstant(constant.Value, out _))
            return Convert(Call(heap, HeapUnsafeGet, Convert(ringVal, typeof(int))), targetType);
        // A nullable target takes the underlying value: a scalar source converts to it,
        // a heap source is the boxed underlying (or the null sentinel).
        if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is not null) {
            if (sourceKind is ValueRepresentationKind.StackScalar && sourceClr is not null)
                return Convert(ScalarValue(ringVal, sourceClr), targetType);
            return Convert(Call(heap, HeapUnsafeGet, Convert(ringVal, typeof(int))), targetType);
        }
        if (targetType.IsValueType
            && ValueRepresentationClassifier.KindFor(targetType) == ValueRepresentationKind.StackScalar)
            return Convert(ringVal, targetType);
        // A scalar reaching an `object` parameter is boxed by its source type. A heap
        // source (a reference, a non-inline value type) is dereferenced.
        if (targetType == typeof(object) && source is not null
            && sourceKind is ValueRepresentationKind.StackScalar && sourceClr is not null)
            return Convert(ScalarValue(ringVal, sourceClr), typeof(object));
        return Convert(Call(heap, HeapUnsafeGet, Convert(ringVal, typeof(int))), targetType);
    }

    /// <summary>The CLR value of a scalar ring slot, at its own declared type.
    /// <c>ulong</c> is an unchecked bitcast of the word, IEEE uses <see cref="FromIeee"/>,
    /// and <c>bool</c> is <c>!= 0</c>.</summary>
    internal static Expression ScalarValue(Expression ringVal, Type sourceClr) =>
        IsIeee(sourceClr)
            ? Convert(FromIeee(ringVal), sourceClr)
            : sourceClr == typeof(bool)
                ? NotEqual(ringVal, Constant(0L))
                : Convert(ringVal, sourceClr);

    /// <summary>Allocate a value on the heap and return its ring handle. Use for
    /// a value whose representation is known to be heap-resident.</summary>
    public static Expression Box(Expression value, Expression heap) =>
        Convert(Call(heap, HeapAllocate, value), typeof(long));

    // ── Runtime conversion ───────────────────────────────────────

    /// <summary>Convert a runtime value to its ring slot — the single runtime
    /// implementation used by <see cref="VmState.SetArgs"/>, restoration, and
    /// the emitted boxing call.</summary>
    public static long ToRing(object? value, VmManagedObjectHeap heap) => value switch {
        null => 0L,
        bool b => b ? 1L : 0L,
        byte b => b,
        sbyte sb => sb,
        short s => s,
        ushort us => us,
        int i => i,
        uint ui => ui,
        long l => l,
        ulong ul => unchecked((long)ul),
        char c => c,
        float f => BitConverter.DoubleToInt64Bits(f),
        double d => BitConverter.DoubleToInt64Bits(d),
        Enum e => EnumWord(e),
        _ => heap.Allocate(value),
    };

    /// <summary>Heap-first runtime conversion used by emitted calls and the
    /// emitter's boxing sites. Delegates to <see cref="ToRing(object?, VmManagedObjectHeap)"/>.</summary>
    public static long BoxToAbi(VmManagedObjectHeap heap, object? value) => ToRing(value, heap);

    /// <summary>Runtime cast of a heap handle to <paramref name="target"/>.
    /// A non-matching value fails closed as an <see cref="InvalidCastException"/>.</summary>
    public static long Cast(VmManagedObjectHeap heap, long raw, Type target) {
        if (raw == 0L) {
            if (target.IsValueType)
                throw new InvalidCastException($"Cannot convert null to '{target}'.");
            return 0L;
        }
        var value = heap.UnsafeGet((int)raw);
        if (value is null) {
            if (target.IsValueType)
                throw new InvalidCastException($"Cannot convert null to '{target}'.");
            return 0L;
        }
        if (target.IsInstanceOfType(value))
            return raw;
        // Entity types publish IDictionary<string, object>. The instance is
        // IDictionary<string, object?> (invariance blocks the CLR cast).
        if (target == typeof(IDictionary<string, object>) && value is IDictionary<string, object?>)
            return raw;
        throw new InvalidCastException($"Cannot convert '{value.GetType()}' to '{target}'.");
    }

    /// <summary>Runtime unbox of a nullable inline value from its heap handle.</summary>
    public static long UnboxNullable(VmManagedObjectHeap heap, long handle) {
        if (handle == 0L) return 0L;
        var obj = heap.UnsafeGet((int)handle);
        if (obj is null) return 0L;
        return System.Convert.ToInt64(obj);
    }

    /// <summary>Runtime unbox of a nullable value as its declared underlying type.
    /// Producers may box a narrower argument (an <c>int</c> passed to a <c>long?</c>
    /// parameter), so the value is converted rather than cast. Handle 0 and a stored
    /// null are the absent sentinel.</summary>
    public static object? UnboxNullableAs(VmManagedObjectHeap heap, long handle, Type underlying) {
        if (handle == 0L) return null;
        var obj = heap.UnsafeGet((int)handle);
        if (obj is null) return null;
        // A box that is already the underlying type (an enum, a decimal) is that
        // value. ChangeType cannot target an enum.
        if (underlying.IsInstanceOfType(obj))
            return obj;
        return System.Convert.ChangeType(obj, underlying, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The boxed value of a required heap slot. Handle 0 is absent and
    /// fails closed; a box that is already <paramref name="type"/> is returned
    /// as-is, which is how an enum box is read (<c>ChangeType</c> cannot target
    /// an enum).</summary>
    public static object RequiredValue(VmManagedObjectHeap heap, long handle, Type type) {
        if (handle == 0L)
            throw new InvalidOperationException($"VM: required {type.Name} value is absent");
        var value = heap.UnsafeGet((int)handle);
        if (value is not null && type.IsInstanceOfType(value))
            return value;
        if (value is null)
            throw new InvalidOperationException($"VM: required {type.Name} value is absent");
        if (type.IsEnum)
            throw new InvalidCastException($"Cannot convert '{value.GetType()}' to '{type}'.");
        return System.Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Runtime <c>as</c>: the same handle when the heap value is
    /// assignable to <paramref name="target"/>, otherwise the null sentinel.</summary>
    public static long TypeAs(VmManagedObjectHeap heap, long raw, Type target) {
        if (raw == 0L) return 0L;
        var obj = heap.UnsafeGet((int)raw);
        return obj is not null && target.IsInstanceOfType(obj) ? raw : 0L;
    }

    // ── Heap shapes ──────────────────────────────────────────────

    /// <summary>Slot 0 of a closure array holds the boxed lambda index.</summary>
    public const int ClosureIndexSlot = 0;

    /// <summary>Index of the first capture cell in a closure array.</summary>
    public const int FirstCaptureIndex = 1;

    /// <summary>Dereference a heap handle as an object — the one deref idiom used
    /// by the emitter (see <see cref="VmManagedObjectHeap.UnsafeGet"/>).</summary>
    public static Expression DerefObject(Expression heap, Expression handle) =>
        Call(heap, HeapUnsafeGet, Convert(handle, typeof(int)));

    /// <summary>Dereference a heap handle as the closure <c>object[]</c>.</summary>
    public static Expression ObjectArray(Expression heap, Expression handle) =>
        Convert(DerefObject(heap, handle), typeof(object[]));

    /// <summary>Dereference a heap handle as a <c>long[]</c> (a VM array of
    /// value-type elements, or a <c>long[1]</c> capture cell).</summary>
    public static Expression LongArray(Expression heap, Expression handle) =>
        Convert(DerefObject(heap, handle), typeof(long[]));

    /// <summary>The <c>long[1]</c> cell a captured variable's slot points at, or a
    /// closure's capture entry. <see cref="SlotLocation.Cell"/> is the one
    /// read/write path for it.</summary>
    public static Expression CellArray(Expression heap, Expression handle) =>
        LongArray(heap, handle);

    /// <summary>The capture cell at <paramref name="captureIndex"/> in a closure.
    /// The <see cref="FirstCaptureIndex"/> header offset is applied here.</summary>
    public static Expression ClosureCell(
        Expression heap, Expression closureHandle, int captureIndex) =>
        Convert(
            ArrayAccess(
                ObjectArray(heap, closureHandle),
                Constant(FirstCaptureIndex + captureIndex)),
            typeof(long[]));

    /// <summary>The VM array storage for an element type: <c>long[]</c> for a
    /// value-type element (a scalar slot or a handle), <c>object[]</c> for a
    /// reference element. Creation-side only; <c>null</c> (unknown) is not an
    /// array shape — see <see cref="ArrayElementRead"/> for the read fallback.</summary>
    public static Type ArraySlotElementType(Type? elementClr) =>
        elementClr is { IsValueType: true } ? typeof(long) : typeof(object);

    /// <summary>The CLR array type a VM array of <paramref name="elementClr"/> is created
    /// as: <c>long[]</c> when the element is a slot word, <c>object[]</c> when it is a
    /// handle. One definition, so a promoted local's type cannot disagree with creation.</summary>
    public static Type ArrayClrType(Type? elementClr) =>
        ArraySlotElementType(elementClr).MakeArrayType();

    /// <summary>Read one element of a VM array. A null
    /// <paramref name="elementClr"/> means the element type is unknown at compile
    /// time, so the arm dispatches on the runtime array shape (<c>long[]</c> vs
    /// <c>object[]</c>).</summary>
    public static Expression ArrayElementRead(
        Expression heap, Expression handle, Expression index, Type? elementClr) {
        var idx = Convert(index, typeof(int));
        if (elementClr is { IsValueType: true }) {
            var longArr = Variable(typeof(long[]), "_longArr");
            return Block([longArr],
                Assign(longArr, LongArray(heap, handle)),
                ArrayAccess(longArr, idx));
        }
        if (elementClr is not null) {
            var objArr = Variable(typeof(object[]), "_objArr");
            return Block([objArr],
                Assign(objArr, ObjectArray(heap, handle)),
                Call(BoxToAbiInfo, heap, ArrayAccess(objArr, idx)));
        }
        var raw = DerefObject(heap, handle);
        var longArrU = Variable(typeof(long[]), "_longArr");
        var objArrU = Variable(typeof(object[]), "_objArr");
        return Condition(
            TypeIs(raw, typeof(long[])),
            Block([longArrU],
                Assign(longArrU, Convert(raw, typeof(long[]))),
                ArrayAccess(longArrU, idx)),
            Block([objArrU],
                Assign(objArrU, Convert(raw, typeof(object[]))),
                Call(BoxToAbiInfo, heap, ArrayAccess(objArrU, idx))));
    }

    /// <summary>Store one element of a VM array. A value-type element
    /// (<c>long[]</c> array) composes with <see cref="CoerceIndexStore"/>; a
    /// reference element (<c>object[]</c> array) boxes. A null
    /// <paramref name="elementClr"/> dispatches on the runtime array shape and
    /// stores the raw slot in the <c>long[]</c> arm.</summary>
    public static Expression ArrayElementWrite(
        Expression heap, Expression handle, Expression index, Expression value,
        Type? elementClr, bool valueIsIeee) {
        var idx = Convert(index, typeof(int));
        if (elementClr is { IsValueType: true }) {
            var longArr = Variable(typeof(long[]), "_assignLongArr");
            return Block([longArr],
                Assign(longArr, LongArray(heap, handle)),
                Assign(ArrayAccess(longArr, idx),
                    CoerceIndexStore(value, elementClr, valueIsIeee)));
        }
        if (elementClr is not null) {
            var objArr = Variable(typeof(object[]), "_assignObjArr");
            return Block([objArr],
                Assign(objArr, ObjectArray(heap, handle)),
                Assign(ArrayAccess(objArr, idx), Convert(value, typeof(object))));
        }
        var raw = DerefObject(heap, handle);
        var longArrU = Variable(typeof(long[]), "_assignLongArr");
        var objArrU = Variable(typeof(object[]), "_assignObjArr");
        return IfThenElse(
            TypeIs(raw, typeof(long[])),
            Block([longArrU],
                Assign(longArrU, Convert(raw, typeof(long[]))),
                Assign(ArrayAccess(longArrU, idx), value)),
            Block([objArrU],
                Assign(objArrU, Convert(raw, typeof(object[]))),
                Assign(ArrayAccess(objArrU, idx), Convert(value, typeof(object)))));
    }

    /// <summary>Store a value into a value-type VM array element: bit-encode an
    /// integer into an IEEE slot, and fail closed on the reverse (where C#
    /// rejects the conversion).</summary>
    public static Expression CoerceIndexStore(
        Expression value, Type elementClr, bool valueIsIeee) {
        bool elementIeee = elementClr == typeof(double) || elementClr == typeof(float);
        if (elementIeee == valueIsIeee)
            return value;
        if (!elementIeee)
            throw new InvalidOperationException(
                $"VM compile rejected: cannot store a floating-point value into a '{elementClr.Name}' array element.");
        return ToIeee(Convert(value, typeof(double)));
    }

    /// <summary>Read one element through a promoted array's typed CLR local: the array
    /// object itself is indexed, so there is no handle deref and no <c>castclass</c>. The
    /// local's own type is the array shape the heap holds — <c>long[]</c> for a value-type
    /// element (its slot word), <c>object[]</c> for a reference element — so this answers
    /// exactly what <see cref="ArrayElementRead"/> answers, without the deref.</summary>
    public static Expression CachedElementRead(
        Expression arrayLocal, Expression index, Expression heap) {
        var idx = Convert(index, typeof(int));
        return arrayLocal.Type == typeof(long[])
            ? ArrayAccess(arrayLocal, idx)
            : Call(BoxToAbiInfo, heap, ArrayAccess(Convert(arrayLocal, typeof(object[])), idx));
    }

    /// <summary>Store one element through a promoted array's typed CLR local. The element
    /// coercion is <see cref="CoerceIndexStore"/>'s, unchanged, so a promoted write encodes
    /// the same bits the heap path wrote.</summary>
    public static Expression CachedElementWrite(
        Expression arrayLocal, Expression index, Expression value,
        Type? elementClr, bool valueIsIeee) {
        var idx = Convert(index, typeof(int));
        if (arrayLocal.Type == typeof(long[]))
            return Assign(ArrayAccess(arrayLocal, idx),
                elementClr is null ? value : CoerceIndexStore(value, elementClr, valueIsIeee));
        return Assign(ArrayAccess(Convert(arrayLocal, typeof(object[])), idx),
            Convert(value, typeof(object)));
    }
}