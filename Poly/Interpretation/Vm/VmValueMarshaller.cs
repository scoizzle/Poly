using System.Reflection;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

/// <summary>
/// LINQ Expression factories for marshalling values between the VM's
/// internal representation (stack scalars as <c>long</c>, heap references
/// as handles) and CLR typed values for external method/constructor calls.
///
/// Consolidates the repeated <c>GetPrimitiveType()?.IsStackValue()</c>
/// dispatch pattern that previously lived inline in multiple locations.
/// </summary>
internal static class VmValueMarshaller {
    /// <summary>
    /// Allocates an object on the VM heap and returns its handle as a <c>long</c>.
    /// Used by <see cref="MarshalFromClr"/> and constructor return paths.
    /// </summary>
    private static readonly MethodInfo HeapAllocate = Ref<Heap>.Method(h => h.Allocate(null));

    /// <summary>
    /// Resolve a raw <c>long</c> from the VM (ring slot or value stack) to a
    /// typed expression suitable as a CLR method or constructor argument.
    ///
    /// <list type="bullet">
    ///   <item>Stack scalars (int, float, etc.) → <c>Convert(rawValue, targetType)</c></item>
    ///   <item>Booleans → <c>rawValue != 0</c></item>
    ///   <item>Heap references → dereference handle via <paramref name="heapRawSlots"/></item>
    /// </list>
    /// </summary>
    /// <param name="rawValue">The <c>long</c> expression from ring or stack.</param>
    /// <param name="targetType">The CLR parameter type to marshal to.</param>
    /// <param name="heapRawSlots">Expression for <c>state.Heap.RawSlots</c>.</param>
    public static Expression MarshalToClr(Expression rawValue, Type targetType, Expression heapRawSlots) {
        var pt = targetType.GetPrimitiveType();
        if (pt is not null && pt.Value.IsStackValue())
            return targetType == typeof(bool)
                ? NotEqual(rawValue, Constant(0L))
                : Convert(rawValue, targetType);

        if (!targetType.IsValueType) {
            var handle = Convert(rawValue, typeof(int));
            return Convert(ArrayAccess(heapRawSlots, handle), targetType);
        }

        return Convert(rawValue, targetType);
    }

    /// <summary>
    /// Convert a CLR return value or field value back to a VM <c>long</c>, either
    /// as a stack scalar or a heap handle.
    ///
    /// <list type="bullet">
    ///   <item>Stack scalars → <c>Convert(clrValue, typeof(long))</c></item>
    ///   <item>Booleans → <c>clrValue ? 1L : 0L</c></item>
    ///   <item>Heap references → <c>heap.Allocate(clrValue)</c>, returning the handle</item>
    /// </list>
    /// </summary>
    /// <param name="clrValue">The CLR expression to marshal.</param>
    /// <param name="sourceType">The CLR return or field type.</param>
    /// <param name="heap">Expression for <c>state.Heap</c>.</param>
    public static Expression MarshalFromClr(Expression clrValue, Type sourceType, Expression heap) {
        var pt = sourceType.GetPrimitiveType();
        if (pt is not null && pt.Value.IsStackValue())
            return sourceType == typeof(bool)
                ? Condition(clrValue, Constant(1L), Constant(0L))
                : sourceType != typeof(long) ? Convert(clrValue, typeof(long)) : clrValue;

        return Convert(Call(heap, HeapAllocate, Convert(clrValue, typeof(object))), typeof(long));
    }
}