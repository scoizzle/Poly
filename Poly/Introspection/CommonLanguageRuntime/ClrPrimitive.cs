namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The CLR host's own table of the types its built-in rules name: C#'s simple types and the handful of other types the
/// CLR's conversion, promotion and operator rules treat as built in. Internal to the CLR host. It is not vocabulary:
/// the neutral contract names no ground type, a consumer asks the target type system's rule what a type is (#998 T8),
/// and another platform has a table of its own or none.
/// </summary>
internal enum ClrPrimitive {
    Boolean,
    Int8,
    Int16,
    Int32,
    Int64,
    UInt8,
    UInt16,
    UInt32,
    UInt64,
    Float32,
    Float64,
    Decimal,
    String,
    Char,
    DateTime,
    DateOnly,
    TimeOnly,
    TimeSpan,
    Guid,
    ByteArray,
    Structure,
    NInt,
    NUInt,
    DateTimeOffset
}

internal static class ClrPrimitiveExtensions {
    /// <summary>True for the fixed-width integers: the exact set that supports integer-only folding. Floating point,
    /// decimal and the native-sized integers are excluded.</summary>
    public static bool IsInteger(this ClrPrimitive id) => id is
        ClrPrimitive.Int8 or ClrPrimitive.Int16 or ClrPrimitive.Int32 or ClrPrimitive.Int64
        or ClrPrimitive.UInt8 or ClrPrimitive.UInt16 or ClrPrimitive.UInt32 or ClrPrimitive.UInt64;

    /// <summary>The name a diagnostic gives the type.</summary>
    public static string GetDisplayName(this ClrPrimitive id) => id is ClrPrimitive.Structure ? "Object" : id.ToString();
}