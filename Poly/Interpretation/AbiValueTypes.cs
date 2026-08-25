namespace Poly.Interpretation;

/// <summary>
/// ABI classification for CLR types in the long-based VM ring: which value
/// types can be inlined as longs (stack scalars) versus which must live on
/// the heap as boxed handles.
///
/// The ring stores every value in a <c>long</c> slot. Integer primitives,
/// bool, char, enums, and nullables of those are stored inline. Float and
/// double use IEEE bit patterns. Decimal and non-numeric value types
/// (DateTime, DateOnly, TimeOnly, TimeSpan, Guid) live on the heap.
/// </summary>
public static class AbiValueTypes {
    /// <summary>True when the CLR type's value can be inlined into the long ring slot.</summary>
    public static bool IsLongRepresentable(Type type) {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t.IsEnum) return true;
        return t == typeof(long) || t == typeof(int) || t == typeof(short)
            || t == typeof(byte) || t == typeof(sbyte) || t == typeof(ushort)
            || t == typeof(uint) || t == typeof(ulong) || t == typeof(char)
            || t == typeof(bool) || t == typeof(float) || t == typeof(double);
    }
}