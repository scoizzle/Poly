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
    Integer = 1 << 2 | Numeric,

    /// <summary>A floating point type (float, double).</summary>
    FloatingPoint = 1 << 3 | Numeric,

    /// <summary>High precision decimal type.</summary>
    HighPrecision = 1 << 4,

    /// <summary>A temporal type (date, time, datetime, timespan).</summary>
    Temporal = 1 << 5,

    /// <summary>An instant type (point in time).</summary>
    Instant = 1 << 6 | Temporal,

    /// <summary>A duration type (timespan, etc).</summary>
    Duration = 1 << 7 | Temporal,

    /// <summary>A text type (string, char).</summary>
    Text = 1 << 8,

    /// <summary>A binary type (byte array).</summary>
    Binary = 1 << 9,

    /// <summary>A nullable/optional type.</summary>
    Nullable = 1 << 10,

    /// <summary>A collection type (array, list, set).</summary>
    Collection = 1 << 11,

    /// <summary>A keyed collection (dictionary, map).</summary>
    Keyed = 1 << 12,

    /// <summary>A reference to another type in the model.</summary>
    Reference = 1 << 13,

    /// <summary>A union/sum type (discriminated union).</summary>
    Union = 1 << 14,

    /// <summary>A product/tuple type.</summary>
    Product = 1 << 15,

    /// <summary>An enumeration type.</summary>
    Enumeration = 1 << 16,

    /// <summary>A flag enumeration type (bit field).</summary>
    FlagEnumeration = 1 << 17 | Enumeration,

    /// <summary>A structured type (JSON, etc).</summary>
    Structured = 1 << 18,

    /// <summary>An identifier type (Guid, etc).</summary>
    Identifier = 1 << 19,

    /// <summary>A signed numeric type.</summary>
    Signed = 1 << 20 | Numeric,

    /// <summary>An unsigned numeric type.</summary>
    Unsigned = 1 << 21 | Numeric,
}

public static class TypeCategoryExtensions {
    extension(TypeCategory category) {
        /// <summary>
        /// Returns true if the specified category flag is set on this type category.
        /// </summary>
        /// <param name="flag">The category flag to check.</param>
        /// <returns>True if the flag is set; otherwise, false.</returns>
        public bool Is(TypeCategory flag) => (category & flag) == flag;

        /// <summary>
        /// Returns true if this type category includes the Nullable flag.
        /// </summary>
        public bool IsNullable => category.Is(TypeCategory.Nullable);

        /// <summary>
        /// Returns true if this type category includes the Collection flag.
        /// </summary>
        public bool IsCollection => category.Is(TypeCategory.Collection);

        /// <summary>
        /// Returns true if this type category includes the Numeric flag.
        /// </summary>
        public bool IsNumeric => category.Is(TypeCategory.Numeric);

        /// <summary>
        /// Returns true if this type category includes the Reference flag.
        /// </summary>
        public bool IsReference => category.Is(TypeCategory.Reference);

        /// <summary>
        /// Returns true if this type category represents an array.
        /// </summary>
        public bool IsArray => category.Is(TypeCategory.Collection) && !category.Is(TypeCategory.Keyed);
    }
}