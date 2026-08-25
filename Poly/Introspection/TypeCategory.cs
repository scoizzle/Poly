namespace Poly.Introspection;

/// <summary>
/// Categories that can be applied to type expressions for classification and querying.
/// Multiple categories can be combined using bitwise operations.
/// </summary>
[Flags]
public enum TypeCategory {
    /// <summary>No category.</summary>
    None = 0,

    // Base atomic types (non-compound primitives)
    /// <summary>A primitive/atomic type (not composed of other types).</summary>
    Primitive = 1 << 0,

    /// <summary>A logical boolean type (true/false).</summary>
    Boolean = 1 << 1 | Primitive,

    /// <summary>A numeric type (integer or floating point).</summary>
    Numeric = 1 << 2,

    /// <summary>A text type (string, char).</summary>
    Text = 1 << 3,

    /// <summary>A binary type (byte array).</summary>
    Binary = 1 << 4,

    /// <summary>A temporal type (date, time, datetime, timespan).</summary>
    Temporal = 1 << 5,

    // Numeric refinements
    /// <summary>An integer type (signed or unsigned).</summary>
    Integer = 1 << 6 | Numeric,

    /// <summary>A floating point type (float, double).</summary>
    FloatingPoint = 1 << 7 | Numeric,

    /// <summary>A signed numeric type.</summary>
    Signed = 1 << 8 | Numeric,

    /// <summary>An unsigned numeric type.</summary>
    Unsigned = 1 << 9 | Numeric,

    /// <summary>High precision decimal type.</summary>
    HighPrecision = 1 << 10,

    // Temporal refinements
    /// <summary>An instant type (point in time).</summary>
    Instant = 1 << 11 | Temporal,

    /// <summary>A duration type (timespan, etc).</summary>
    Duration = 1 << 12 | Temporal,

    /// <summary>A date-only type (no time component).</summary>
    DateOnly = 1 << 13 | Temporal,

    /// <summary>A time-of-day type (no date component).</summary>
    TimeOfDay = 1 << 14 | Temporal,

    /// <summary>A full datetime type (date and time).</summary>
    DateTime = 1 << 15 | Instant,

    // Collection and modifier flags
    /// <summary>A nullable/optional type.</summary>
    Nullable = 1 << 16,

    /// <summary>A collection type (array, list, set).</summary>
    Collection = 1 << 17,

    /// <summary>A keyed collection (dictionary, map).</summary>
    Keyed = 1 << 18 | Collection,

    // References and complex types
    /// <summary>A reference to another type in the model.</summary>
    Reference = 1 << 19,

    /// <summary>An enumeration type.</summary>
    Enumeration = 1 << 20,

    /// <summary>A flag enumeration type (bit field).</summary>
    FlagEnumeration = 1 << 21 | Enumeration,

    /// <summary>A union/sum type (discriminated union).</summary>
    Union = 1 << 22,

    /// <summary>A product/tuple type.</summary>
    Product = 1 << 23,

    /// <summary>A structured type (JSON, etc).</summary>
    Structured = 1 << 24,

    /// <summary>An identifier type (Guid, etc).</summary>
    Identifier = 1 << 25,
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

        /// <summary>
        /// Returns true if this type category includes the Boolean flag.
        /// </summary>
        public bool IsBoolean => category.Is(TypeCategory.Boolean);

        /// <summary>
        /// Returns true if this type category includes the DateOnly flag.
        /// </summary>
        public bool IsDateOnly => category.Is(TypeCategory.DateOnly);

        /// <summary>
        /// Returns true if this type category includes the TimeOfDay flag.
        /// </summary>
        public bool IsTimeOfDay => category.Is(TypeCategory.TimeOfDay);

        /// <summary>
        /// Returns true if this type category includes the DateTime flag.
        /// </summary>
        public bool IsDateTime => category.Is(TypeCategory.DateTime);
    }
}