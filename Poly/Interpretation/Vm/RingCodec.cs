using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

/// <summary>
/// The single definition of the VM's long-slot ABI: how a value is classified
/// (inline scalar, boolean, IEEE bit pattern, heap handle) and converted in both
/// directions — at compile time (<see cref="Expression"/> factories) and at
/// runtime (<see cref="ToRing(object?, Heap)"/> and friends).
/// </summary>
/// <remarks>
/// Every ABI site delegates here. Analysis passes use <see cref="IsInline(Type)"/>
/// to predict the same classification the emitter applies. Handle 0 is the ABI
/// null sentinel; it is never a live object.
/// </remarks>
internal static class RingCodec {
    private static readonly MethodInfo HeapUnsafeGet = Ref<Heap>.Method(h => h.UnsafeGet(0));
    private static readonly MethodInfo HeapAllocate = Ref<Heap>.Method(h => h.Allocate(default!));
    private static readonly MethodInfo BoxToAbiInfo =
        Ref.Method((Expression<Func<Heap, object?, long>>)((h, v) => BoxToAbi(h, v)));
    private static readonly MethodInfo BitConverterInt64BitsToDouble =
        Ref.Method((Expression<Func<long, double>>)(v => BitConverter.Int64BitsToDouble(v)));
    private static readonly MethodInfo BitConverterDoubleToInt64Bits =
        Ref.Method((Expression<Func<double, long>>)(v => BitConverter.DoubleToInt64Bits(v)));

    // ── Classification ───────────────────────────────────────────

    /// <summary>The ring-slot representation of a CLR type. Nullable of an inline
    /// type stays inline; enums are inline scalars; float and double are inline
    /// IEEE bit patterns (classified <see cref="ValueRepresentationKind.StackScalar"/>
    /// because they occupy one slot — <see cref="IsIeee(Type)"/> carries the
    /// bit-pattern distinction).</summary>
    public static ValueRepresentationKind KindFor(Type type) {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t.IsEnum) return ValueRepresentationKind.StackScalar;
        if (t == typeof(bool)) return ValueRepresentationKind.Bool;
        if (t == typeof(long) || t == typeof(int) || t == typeof(short)
            || t == typeof(byte) || t == typeof(sbyte) || t == typeof(ushort)
            || t == typeof(uint) || t == typeof(ulong) || t == typeof(char)
            || t == typeof(float) || t == typeof(double))
            return ValueRepresentationKind.StackScalar;
        return ValueRepresentationKind.HeapRef;
    }

    /// <summary>True when the type's value lives inline in the ring slot rather
    /// than on the heap as a handle.</summary>
    public static bool IsInline(Type type) =>
        KindFor(type) is ValueRepresentationKind.StackScalar or ValueRepresentationKind.Bool;

    /// <summary>True when the type stores its IEEE bit pattern in the slot.
    /// Exact <c>float</c>/<c>double</c> only; a nullable wraps the value in a
    /// heap cell, matching <see cref="FromClr"/>.</summary>
    public static bool IsIeee(Type type) =>
        type == typeof(float) || type == typeof(double);

    /// <summary>The slot kind a compile-time constant is stored as: derived from
    /// <see cref="TryInlineConstant"/>, with the two representational exceptions —
    /// <c>null</c> is the heap null sentinel (it folds to 0 but is not a scalar
    /// value), and <c>bool</c> keeps its own kind.</summary>
    public static ValueRepresentationKind KindFor(object? constant) {
        if (constant is null) return ValueRepresentationKind.HeapRef;
        if (constant is bool) return ValueRepresentationKind.Bool;
        return TryInlineConstant(constant, out _)
            ? ValueRepresentationKind.StackScalar
            : ValueRepresentationKind.HeapRef;
    }

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
            default: raw = 0; return false;
        }
    }

    /// <summary>True when a node's value is an IEEE bit pattern (float/double).</summary>
    public static bool IsIeee(Node node, AnalysisResult? analysis) {
        var clr = analysis?.GetMetadata<ValueRepresentationMetadata>(node)?.ClrType;
        return clr is not null && IsIeee(clr);
    }

    /// <summary>True when a node's slot uses the ABI null sentinel: a literal
    /// null, a heap reference, or a nullable value type.</summary>
    public static bool UsesNullSentinel(Node node, AnalysisResult? analysis) {
        if (node is Constant { Value: null }) return true;
        var meta = analysis?.GetMetadata<ValueRepresentationMetadata>(node);
        if (meta?.Kind == ValueRepresentationKind.HeapRef) return true;
        return meta?.ClrType is Type t && Nullable.GetUnderlyingType(t) is not null;
    }

    /// <summary>Whether a call argument sits in its slot as a raw scalar (not a
    /// heap handle). The value representation decides; without one, the
    /// argument's resolved type does.</summary>
    public static bool IsStackScalar(Node node, AnalysisResult? analysis) {
        switch (analysis?.GetValueRepresentation(node)) {
            case ValueRepresentationKind.StackScalar or ValueRepresentationKind.Bool:
                return true;
            case ValueRepresentationKind.HeapRef:
                return false;
        }
        var type = analysis?.GetResolvedType(node)
            ?? throw new InvalidOperationException(
                $"VM compile rejected: cannot tell whether argument '{node}' is a scalar or a reference.");
        return type.GetRuntimeType() is { IsValueType: true };
    }

    /// <summary>Whether an argument to the name-dispatched invoke path sits in its
    /// ring slot as a scalar (boxed by its source type) rather than a heap handle.
    /// Unlike <see cref="IsStackScalar"/>, a
    /// <see cref="ValueRepresentationKind.HeapRef"/> classification does not
    /// suppress the syntactic arm: a string-concat or decimal arithmetic node is
    /// classified HeapRef yet is still scalar-boxed here. This predicate never
    /// throws.</summary>
    public static bool IsInvokeArgumentScalar(Node node, AnalysisResult? analysis) {
        if (analysis?.GetValueRepresentation(node)
            is ValueRepresentationKind.StackScalar or ValueRepresentationKind.Bool)
            return true;
        return node is Poly.Ast.Nodes.Add or Poly.Ast.Nodes.Subtract
            or Poly.Ast.Nodes.Multiply or Poly.Ast.Nodes.Divide or Poly.Ast.Nodes.Modulo
            || node is Constant { Value: long or int or bool or byte or short or sbyte };
    }

    // ── Compile-time conversion ──────────────────────────────────

    /// <summary>Convert a CLR-typed expression into its ring slot: IEEE bitcast
    /// for float/double, widening for inline scalars, heap handle otherwise.</summary>
    public static Expression FromClr(Expression value, Type? clrType, Expression heap) {
        if (clrType == typeof(float) || clrType == typeof(double))
            return Call(BitConverterDoubleToInt64Bits, Convert(value, typeof(double)));
        if (clrType is not null && clrType.IsValueType && IsInline(clrType))
            return Convert(Convert(value, clrType), typeof(long));
        return Call(BoxToAbiInfo, heap, Convert(value, typeof(object)));
    }

    /// <summary>Read a ring slot as a CLR value of <paramref name="targetType"/>.
    /// A scalar reaching an <c>object</c> parameter is boxed by its source type,
    /// never mistaken for a heap handle.</summary>
    public static Expression ToClr(
        Expression ringVal, Type targetType, Node? source, AnalysisResult? analysis, Expression heap) {
        if (targetType == typeof(double) || targetType == typeof(float)) {
            var srcType = source is Constant c ? c.Value?.GetType()
                : analysis?.GetMetadata<ValueRepresentationMetadata>(source)?.ClrType;
            if (srcType is not null && IsIeee(srcType))
                return Convert(Call(BitConverterInt64BitsToDouble, ringVal), targetType);
            return Convert(ringVal, targetType);
        }
        if (targetType == typeof(bool))
            return NotEqual(ringVal, Constant(0L));
        if (targetType.IsValueType && IsInline(targetType))
            return Convert(ringVal, targetType);
        // A *scalar* argument reaching an `object` parameter must be boxed — its ring
        // slot holds the raw value, not a handle. Anything else (a reference, or a
        // value type that is a heap handle such as DateOnly/DateTime/decimal/nullable)
        // is a handle and is dereferenced. The test is the argument's representation
        // kind, not `IsValueType`: `int?` is a value type but its slot is a handle.
        if (targetType == typeof(object) && source is not null) {
            var kind = source is Constant constant
                ? KindFor(constant.Value)
                : analysis?.GetValueRepresentation(source);
            if (kind is ValueRepresentationKind.StackScalar or ValueRepresentationKind.Bool) {
                var sourceClr = source is Constant c2 && c2.Value is not null
                    ? c2.Value.GetType()
                    : analysis?.GetMetadata<ValueRepresentationMetadata>(source)?.ClrType
                        ?? analysis?.GetResolvedType(source)?.GetRuntimeType();
                if (sourceClr is not null) {
                    Expression scalar = IsIeee(sourceClr)
                        ? Convert(Call(BitConverterInt64BitsToDouble, ringVal), sourceClr)
                        : sourceClr == typeof(bool)
                            ? NotEqual(ringVal, Constant(0L))
                            : Convert(ringVal, sourceClr);
                    return Convert(scalar, typeof(object));
                }
            }
        }
        return Convert(Call(heap, HeapUnsafeGet, Convert(ringVal, typeof(int))), targetType);
    }

    /// <summary>Allocate a value on the heap and return its ring handle. Use for
    /// a value whose representation is known to be heap-resident.</summary>
    public static Expression Box(Expression value, Expression heap) =>
        Convert(Call(heap, HeapAllocate, value), typeof(long));

    // ── Runtime conversion ───────────────────────────────────────

    /// <summary>Convert a runtime value to its ring slot — the single runtime
    /// implementation used by <see cref="VmState.SetArgs"/>, restoration, and
    /// the emitted boxing call.</summary>
    public static long ToRing(object? value, Heap heap) => value switch {
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
        _ => heap.Allocate(value),
    };

    /// <summary>Heap-first runtime conversion used by emitted calls and the
    /// emitter's boxing sites. Delegates to <see cref="ToRing(object?, Heap)"/>.</summary>
    public static long BoxToAbi(Heap heap, object? value) => ToRing(value, heap);

    /// <summary>Runtime cast of a heap handle to <paramref name="target"/>.
    /// A non-matching value fails closed as an <see cref="InvalidCastException"/>.</summary>
    public static long Cast(Heap heap, long raw, Type target) {
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
    public static long UnboxNullable(Heap heap, long handle) {
        if (handle == 0L) return 0L;
        var obj = heap.UnsafeGet((int)handle);
        if (obj is null) return 0L;
        return System.Convert.ToInt64(obj);
    }

    /// <summary>Runtime <c>as</c>: the same handle when the heap value is
    /// assignable to <paramref name="target"/>, otherwise the null sentinel.</summary>
    public static long TypeAs(Heap heap, long raw, Type target) {
        if (raw == 0L) return 0L;
        var obj = heap.UnsafeGet((int)raw);
        return obj is not null && target.IsInstanceOfType(obj) ? raw : 0L;
    }
}