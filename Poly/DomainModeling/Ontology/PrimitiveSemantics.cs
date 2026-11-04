namespace Poly.DomainModeling.Ontology;

/// <summary>
/// The closed set of meanings a domain <see cref="PrimitiveType"/> can carry.
/// Domain-owned: this is domain vocabulary, not a core type shape, so the
/// introspection layer never sees it. One value per primitive, so combinations
/// such as "number and decimal" cannot be expressed.
/// </summary>
public enum PrimitiveSemantics {
    /// <summary>Logical true/false.</summary>
    Boolean,

    /// <summary>An integral number.</summary>
    Number,

    /// <summary>A high-precision decimal.</summary>
    Decimal,

    /// <summary>Text.</summary>
    Text,

    /// <summary>A globally unique identifier.</summary>
    Uuid,

    /// <summary>Opaque binary data.</summary>
    Binary,

    /// <summary>A calendar date with no time component.</summary>
    Date,

    /// <summary>A time of day with no date component.</summary>
    Time,

    /// <summary>A date and a time.</summary>
    DateTime,

    /// <summary>A length of time.</summary>
    Duration
}