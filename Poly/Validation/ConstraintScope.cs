namespace Poly.Validation;

/// <summary>
/// Classifies constraints by the evaluation scope they require.
/// This determines when, where, and with what infrastructure a constraint can be checked.
/// </summary>
public enum ConstraintScope {
    /// <summary>
    /// Evaluable from the property value alone.
    /// Examples: type checks, nullability, length, range.
    /// Can be checked client-side, requires no external state.
    /// </summary>
    Structural,

    /// <summary>
    /// Requires sibling property values on the same entity instance.
    /// Examples: EndDate > StartDate, "if Status = Active then AssignedTo is required."
    /// Implies access to the full entity during validation.
    /// </summary>
    IntraEntity,

    /// <summary>
    /// Requires access to other instances of the same type.
    /// Examples: uniqueness constraints, "no overlapping date ranges."
    /// Implies a query or index against the entity's collection.
    /// </summary>
    InterEntity,

    /// <summary>
    /// Requires access to instances of other types.
    /// Examples: referential integrity, cardinality limits, "Company must exist."
    /// Implies cross-collection or cross-table queries; must be enforced transactionally.
    /// </summary>
    CrossEntity,

    /// <summary>
    /// Requires access to current or historical state over time.
    /// Examples: "cannot re-hire within 30 days of termination," "value must not decrease."
    /// Implies temporal queries or event history access.
    /// </summary>
    Temporal
}