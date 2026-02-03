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
        PrimitiveTypeId.Boolean => "Boolean",
        PrimitiveTypeId.Int8 => "Int8",
        PrimitiveTypeId.Int16 => "Int16",
        PrimitiveTypeId.Int32 => "Int32",
        PrimitiveTypeId.Int64 => "Int64",
        PrimitiveTypeId.UInt8 => "UInt8",
        PrimitiveTypeId.UInt16 => "UInt16",
        PrimitiveTypeId.UInt32 => "UInt32",
        PrimitiveTypeId.UInt64 => "UInt64",
        PrimitiveTypeId.Float32 => "Float32",
        PrimitiveTypeId.Float64 => "Float64",
        PrimitiveTypeId.Decimal => "Decimal",
        PrimitiveTypeId.String => "String",
        PrimitiveTypeId.Char => "Char",
        PrimitiveTypeId.DateTime => "DateTime",
        PrimitiveTypeId.DateOnly => "DateOnly",
        PrimitiveTypeId.TimeOnly => "TimeOnly",
        PrimitiveTypeId.TimeSpan => "TimeSpan",
        PrimitiveTypeId.Guid => "Guid",
        PrimitiveTypeId.ByteArray => "ByteArray",
        PrimitiveTypeId.Json => "Json",
        _ => id.ToString()
    };

    /// <summary>
    /// Gets the corresponding CLR <see cref="Type"/> for this primitive type.
    /// Returns null for types without a direct CLR mapping.
    /// </summary>
    public static Type? GetClrType(this PrimitiveTypeId id) => id switch {
        PrimitiveTypeId.Boolean => typeof(bool),
        PrimitiveTypeId.Int8 => typeof(sbyte),
        PrimitiveTypeId.Int16 => typeof(short),
        PrimitiveTypeId.Int32 => typeof(int),
        PrimitiveTypeId.Int64 => typeof(long),
        PrimitiveTypeId.UInt8 => typeof(byte),
        PrimitiveTypeId.UInt16 => typeof(ushort),
        PrimitiveTypeId.UInt32 => typeof(uint),
        PrimitiveTypeId.UInt64 => typeof(ulong),
        PrimitiveTypeId.Float32 => typeof(float),
        PrimitiveTypeId.Float64 => typeof(double),
        PrimitiveTypeId.Decimal => typeof(decimal),
        PrimitiveTypeId.String => typeof(string),
        PrimitiveTypeId.Char => typeof(char),
        PrimitiveTypeId.DateTime => typeof(DateTime),
        PrimitiveTypeId.DateOnly => typeof(DateOnly),
        PrimitiveTypeId.TimeOnly => typeof(TimeOnly),
        PrimitiveTypeId.TimeSpan => typeof(TimeSpan),
        PrimitiveTypeId.Guid => typeof(Guid),
        PrimitiveTypeId.ByteArray => typeof(byte[]),
        PrimitiveTypeId.Json => typeof(object), // JSON maps to object
        _ => null
    };
}