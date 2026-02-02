namespace Poly.Introspection;

/// <summary>
/// Categories that can be applied to type expressions for classification and querying.
/// Multiple categories can be combined using bitwise operations.
/// </summary>
[Flags]
public enum TypeCategory {
    /// <summary>No category.</summary>
    None = 0,

    /// <summary>A primitive/atomic type (not composed of other types).</summary>
    Primitive = 1 << 0,

    /// <summary>A numeric type (integer or floating point).</summary>
    Numeric = 1 << 1,

    /// <summary>An integer type (signed or unsigned).</summary>
    Integer = 1 << 2,

    /// <summary>A floating point type (float, double).</summary>
    FloatingPoint = 1 << 3,

    /// <summary>High precision decimal type.</summary>
    HighPrecision = 1 << 4,

    /// <summary>A temporal type (date, time, datetime, timespan).</summary>
    Temporal = 1 << 5,

    /// <summary>A text type (string, char).</summary>
    Text = 1 << 6,

    /// <summary>A binary type (byte array).</summary>
    Binary = 1 << 7,

    /// <summary>A nullable/optional type.</summary>
    Nullable = 1 << 8,

    /// <summary>A collection type (array, list, set).</summary>
    Collection = 1 << 9,

    /// <summary>A keyed collection (dictionary, map).</summary>
    Keyed = 1 << 10,

    /// <summary>A reference to another type in the model.</summary>
    Reference = 1 << 11,

    /// <summary>A union/sum type (discriminated union).</summary>
    Union = 1 << 12,

    /// <summary>A product/tuple type.</summary>
    Product = 1 << 13,

    /// <summary>An enumeration type.</summary>
    Enumeration = 1 << 14,

    /// <summary>A structured type (JSON, etc).</summary>
    Structured = 1 << 15,

    /// <summary>An identifier type (Guid, etc).</summary>
    Identifier = 1 << 16,

    /// <summary>A signed numeric type.</summary>
    Signed = 1 << 17,

    /// <summary>An unsigned numeric type.</summary>
    Unsigned = 1 << 18
}