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
    Structure
}

/// <summary>
/// Extension methods for PrimitiveType.
/// </summary>
public static class PrimitiveTypeExtensions {
    /// <summary>
    /// Gets the type categories that apply to this primitive type.
    /// </summary>
    public static TypeCategory GetCategory(this PrimitiveType id) => id switch {
        PrimitiveType.Boolean => TypeCategory.Primitive,

        PrimitiveType.Int8 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Signed,
        PrimitiveType.Int16 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Signed,
        PrimitiveType.Int32 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Signed,
        PrimitiveType.Int64 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Signed,

        PrimitiveType.UInt8 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Unsigned,
        PrimitiveType.UInt16 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Unsigned,
        PrimitiveType.UInt32 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Unsigned,
        PrimitiveType.UInt64 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Unsigned,

        PrimitiveType.Float32 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.FloatingPoint | TypeCategory.Signed,
        PrimitiveType.Float64 => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.FloatingPoint | TypeCategory.Signed,
        PrimitiveType.Decimal => TypeCategory.Primitive | TypeCategory.Numeric | TypeCategory.HighPrecision | TypeCategory.Signed,

        PrimitiveType.String => TypeCategory.Primitive | TypeCategory.Text,
        PrimitiveType.Char => TypeCategory.Primitive | TypeCategory.Text,

        PrimitiveType.DateTime => TypeCategory.Primitive | TypeCategory.Temporal,
        PrimitiveType.DateOnly => TypeCategory.Primitive | TypeCategory.Temporal,
        PrimitiveType.TimeOnly => TypeCategory.Primitive | TypeCategory.Temporal,
        PrimitiveType.TimeSpan => TypeCategory.Primitive | TypeCategory.Temporal,

        PrimitiveType.Guid => TypeCategory.Primitive | TypeCategory.Identifier,
        PrimitiveType.ByteArray => TypeCategory.Primitive | TypeCategory.Binary,
        PrimitiveType.Structure => TypeCategory.Primitive | TypeCategory.Structured,

        _ => TypeCategory.Primitive
    };

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
        _ => id.ToString()
    };

    /// <summary>
    /// Gets the corresponding CLR <see cref="Type"/> for this primitive type.
    /// Returns null for types without a direct CLR mapping.
    /// </summary>
    public static Type? GetClrType(this PrimitiveType id) => id switch {
        PrimitiveType.Boolean => typeof(bool),
        PrimitiveType.Int8 => typeof(sbyte),
        PrimitiveType.Int16 => typeof(short),
        PrimitiveType.Int32 => typeof(int),
        PrimitiveType.Int64 => typeof(long),
        PrimitiveType.UInt8 => typeof(byte),
        PrimitiveType.UInt16 => typeof(ushort),
        PrimitiveType.UInt32 => typeof(uint),
        PrimitiveType.UInt64 => typeof(ulong),
        PrimitiveType.Float32 => typeof(float),
        PrimitiveType.Float64 => typeof(double),
        PrimitiveType.Decimal => typeof(decimal),
        PrimitiveType.String => typeof(string),
        PrimitiveType.Char => typeof(char),
        PrimitiveType.DateTime => typeof(DateTime),
        PrimitiveType.DateOnly => typeof(DateOnly),
        PrimitiveType.TimeOnly => typeof(TimeOnly),
        PrimitiveType.TimeSpan => typeof(TimeSpan),
        PrimitiveType.Guid => typeof(Guid),
        PrimitiveType.ByteArray => typeof(byte[]),
        PrimitiveType.Structure => typeof(object),
        _ => null
    };
}