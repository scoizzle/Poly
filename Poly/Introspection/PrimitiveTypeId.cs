namespace Poly.Introspection;

/// <summary>
/// Identifies a primitive (leaf) type in the type expression system.
/// These are the atomic building blocks from which all other types are composed.
/// </summary>
public enum PrimitiveTypeId {
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
    Json
}

/// <summary>
/// Extension methods for PrimitiveTypeId.
/// </summary>
public static class PrimitiveTypeIdExtensions {
    /// <summary>
    /// Gets the type categories that apply to this primitive type.
    /// </summary>
    public static TypeCategory GetCategory(this PrimitiveTypeId id) => id switch {
        PrimitiveTypeId.Boolean => TypeCategory.Primitive,

        PrimitiveTypeId.Int8 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Signed,
        PrimitiveTypeId.Int16 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Signed,
        PrimitiveTypeId.Int32 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Signed,
        PrimitiveTypeId.Int64 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Signed,

        PrimitiveTypeId.UInt8 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Unsigned,
        PrimitiveTypeId.UInt16 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Unsigned,
        PrimitiveTypeId.UInt32 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Unsigned,
        PrimitiveTypeId.UInt64 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Unsigned,

        PrimitiveTypeId.Float32 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.FloatingPoint | TypeCategory.Signed,
        PrimitiveTypeId.Float64 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.FloatingPoint | TypeCategory.Signed,
        PrimitiveTypeId.Decimal => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.HighPrecision | TypeCategory.Signed,

        PrimitiveTypeId.String => TypeCategory.Primitive | TypeCategory.Text,
        PrimitiveTypeId.Char => TypeCategory.Primitive | TypeCategory.Text,

        PrimitiveTypeId.DateTime => TypeCategory.Primitive | TypeCategory.Temporal,
        PrimitiveTypeId.DateOnly => TypeCategory.Primitive | TypeCategory.Temporal,
        PrimitiveTypeId.TimeOnly => TypeCategory.Primitive | TypeCategory.Temporal,
        PrimitiveTypeId.TimeSpan => TypeCategory.Primitive | TypeCategory.Temporal,

        PrimitiveTypeId.Guid => TypeCategory.Primitive | TypeCategory.Identifier,
        PrimitiveTypeId.ByteArray => TypeCategory.Primitive | TypeCategory.Binary,
        PrimitiveTypeId.Json => TypeCategory.Primitive | TypeCategory.Structured,

        _ => TypeCategory.Primitive
    };

    /// <summary>
    /// Gets a human-readable display name for this primitive type.
    /// </summary>
    public static string GetDisplayName(this PrimitiveTypeId id) => id switch {
        PrimitiveTypeId.Boolean => "bool",
        PrimitiveTypeId.Int8 => "sbyte",
        PrimitiveTypeId.Int16 => "short",
        PrimitiveTypeId.Int32 => "int",
        PrimitiveTypeId.Int64 => "long",
        PrimitiveTypeId.UInt8 => "byte",
        PrimitiveTypeId.UInt16 => "ushort",
        PrimitiveTypeId.UInt32 => "uint",
        PrimitiveTypeId.UInt64 => "ulong",
        PrimitiveTypeId.Float32 => "float",
        PrimitiveTypeId.Float64 => "double",
        PrimitiveTypeId.Decimal => "decimal",
        PrimitiveTypeId.String => "string",
        PrimitiveTypeId.Char => "char",
        PrimitiveTypeId.DateTime => "DateTime",
        PrimitiveTypeId.DateOnly => "DateOnly",
        PrimitiveTypeId.TimeOnly => "TimeOnly",
        PrimitiveTypeId.TimeSpan => "TimeSpan",
        PrimitiveTypeId.Guid => "Guid",
        PrimitiveTypeId.ByteArray => "byte[]",
        PrimitiveTypeId.Json => "json",
        _ => id.ToString()
    };
}