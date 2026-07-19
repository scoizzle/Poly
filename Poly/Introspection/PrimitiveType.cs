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
    /// Gets the C# keyword equivalent for a primitive type.
    /// Used by <c>CSharpGenerator</c> to emit idiomatic C# types.
    /// </summary>
    public static string GetCSharpKeyword(this PrimitiveType id) => id switch {
        PrimitiveType.Boolean => "bool",
        PrimitiveType.Int8 => "sbyte",
        PrimitiveType.Int16 => "short",
        PrimitiveType.Int32 => "int",
        PrimitiveType.Int64 => "long",
        PrimitiveType.UInt8 => "byte",
        PrimitiveType.UInt16 => "ushort",
        PrimitiveType.UInt32 => "uint",
        PrimitiveType.UInt64 => "ulong",
        PrimitiveType.Float32 => "float",
        PrimitiveType.Float64 => "double",
        PrimitiveType.Decimal => "decimal",
        PrimitiveType.String => "string",
        PrimitiveType.Char => "char",
        PrimitiveType.DateTime => "DateTime",
        PrimitiveType.DateOnly => "DateOnly",
        PrimitiveType.TimeOnly => "TimeOnly",
        PrimitiveType.TimeSpan => "TimeSpan",
        PrimitiveType.Guid => "Guid",
        PrimitiveType.ByteArray => "byte[]",
        PrimitiveType.Structure => "object",
        _ => id.ToString()
    };

    /// <summary>
    /// Returns true when this primitive type can be stored directly on an
    /// evaluation-stack slot (e.g. a 64-bit register) without heap indirection.
    /// For example, numeric primitives and booleans are stack values; strings
    /// and structured types require heap handles.
    /// </summary>
    public static bool IsStackValue(this PrimitiveType id) => id switch {
        PrimitiveType.Int64 or PrimitiveType.Int32 or PrimitiveType.Int16 or PrimitiveType.Int8
        or PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32
        or PrimitiveType.Boolean or PrimitiveType.Float32 or PrimitiveType.Float64
        => true,
        _ => false
    };

    /// <summary>
    /// Returns the <see cref="PrimitiveType"/> that corresponds to <paramref name="type"/>,
    /// or null if the type is not a recognized primitive.
    /// </summary>
    public static PrimitiveType? GetPrimitiveType(this Type type) => type switch {
        Type t when t == typeof(bool) => PrimitiveType.Boolean,
        Type t when t == typeof(sbyte) => PrimitiveType.Int8,
        Type t when t == typeof(short) => PrimitiveType.Int16,
        Type t when t == typeof(int) => PrimitiveType.Int32,
        Type t when t == typeof(long) => PrimitiveType.Int64,
        Type t when t == typeof(byte) => PrimitiveType.UInt8,
        Type t when t == typeof(ushort) => PrimitiveType.UInt16,
        Type t when t == typeof(uint) => PrimitiveType.UInt32,
        Type t when t == typeof(ulong) => PrimitiveType.UInt64,
        Type t when t == typeof(float) => PrimitiveType.Float32,
        Type t when t == typeof(double) => PrimitiveType.Float64,
        Type t when t == typeof(decimal) => PrimitiveType.Decimal,
        Type t when t == typeof(string) => PrimitiveType.String,
        Type t when t == typeof(char) => PrimitiveType.Char,
        Type t when t == typeof(DateTime) => PrimitiveType.DateTime,
        Type t when t == typeof(DateOnly) => PrimitiveType.DateOnly,
        Type t when t == typeof(TimeOnly) => PrimitiveType.TimeOnly,
        Type t when t == typeof(TimeSpan) => PrimitiveType.TimeSpan,
        Type t when t == typeof(Guid) => PrimitiveType.Guid,
        Type t when t == typeof(byte[]) => PrimitiveType.ByteArray,
        _ => null
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