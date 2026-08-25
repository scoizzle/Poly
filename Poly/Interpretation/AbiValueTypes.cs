namespace Poly.Interpretation;

/// <summary>
/// ABI classification for CLR types in the long-based VM ring: which value
/// types can be inlined as longs (stack scalars) versus which must live on
/// the heap as boxed handles.
///
/// The ring stores every value in a <c>long</c> slot. Numeric primitives,
/// bool, char, enums, decimals, and nullables of those all have a defined
/// conversion to <c>long</c> and are stored inline. Non-numeric value types
/// (DateTime, DateOnly, TimeOnly, TimeSpan, Guid) have no <c>long</c>
/// conversion — their member values are boxed and allocated on the heap, and
/// the ring slot holds a heap handle exactly like a reference type.
/// </summary>
public static class AbiValueTypes {
    /// <summary>True when the CLR type's value can be inlined into the long ring slot.</summary>
    public static bool IsLongRepresentable(Type type) {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t.IsEnum) return true;
        return t == typeof(long) || t == typeof(int) || t == typeof(short)
            || t == typeof(byte) || t == typeof(sbyte) || t == typeof(ushort)
            || t == typeof(uint) || t == typeof(ulong) || t == typeof(char)
            || t == typeof(bool) || t == typeof(float) || t == typeof(double)
            || t == typeof(decimal);
    }
}