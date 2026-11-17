namespace Poly.Introspection;

/// <summary>
/// Identifies a primitive (leaf) type in the type expression system.
/// These are the atomic building blocks from which all other types are composed.
/// </summary>
public enum PrimitiveType {
    // Boolean
    Boolean,

    // Signed integers
    Int8,
    Int16,
    Int32,
    Int64,

    // Unsigned integers
    UInt8,
    UInt16,
    UInt32,
    UInt64,

    // Floating point
    Float32,
    Float64,

    // Decimal (high precision)
    Decimal,

    // Text
    String,
    Char,

    // Temporal
    DateTime,
    DateOnly,
    TimeOnly,
    TimeSpan,

    // Identifiers
    Guid,

    // Binary
    ByteArray,

    // Structured
    Structure,

    // Native-sized integers (C# 11 nint and nuint), appended so no earlier member's ordinal moves
    NInt,
    NUInt,

    // Language types the CLR has no runtime type for (a protocol host's own primitives: ECMAScript's Undefined, Null,
    // Symbol and BigInt), appended so no earlier member's ordinal moves. A platform maps only the primitives it has;
    // ClrPrimitiveTypes answers no CLR type for these.
    Undefined,
    Null,
    Symbol,
    BigInt
}

/// <summary>
/// Extension methods for PrimitiveType.
/// </summary>
public static class PrimitiveTypeExtensions {
    /// <summary>
    /// Returns true for the integral primitives — the exact set that supports
    /// integer-only folding. Floating point and decimal are excluded.
    /// </summary>
    public static bool IsInteger(this PrimitiveType id) => id is
        PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32 or PrimitiveType.Int64
        or PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32 or PrimitiveType.UInt64;

    /// <summary>
    /// Gets a human-readable display name for this primitive type.
    /// </summary>
    public static string GetDisplayName(this PrimitiveType id) => id switch {
        PrimitiveType.Boolean => "Boolean",
        PrimitiveType.Int8 => "Int8",
        PrimitiveType.Int16 => "Int16",
        PrimitiveType.Int32 => "Int32",
        PrimitiveType.Int64 => "Int64",
        PrimitiveType.UInt8 => "UInt8",
        PrimitiveType.UInt16 => "UInt16",
        PrimitiveType.UInt32 => "UInt32",
        PrimitiveType.UInt64 => "UInt64",
        PrimitiveType.Float32 => "Float32",
        PrimitiveType.Float64 => "Float64",
        PrimitiveType.Decimal => "Decimal",
        PrimitiveType.String => "String",
        PrimitiveType.Char => "Char",
        PrimitiveType.DateTime => "DateTime",
        PrimitiveType.DateOnly => "DateOnly",
        PrimitiveType.TimeOnly => "TimeOnly",
        PrimitiveType.TimeSpan => "TimeSpan",
        PrimitiveType.Guid => "Guid",
        PrimitiveType.ByteArray => "ByteArray",
        PrimitiveType.Structure => "Object",
        PrimitiveType.NInt => "NInt",
        PrimitiveType.NUInt => "NUInt",
        _ => id.ToString()
    };

}