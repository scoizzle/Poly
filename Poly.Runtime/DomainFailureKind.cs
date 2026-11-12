namespace Poly.Runtime;

/// <summary>
/// The closed, append-only set of domain failure kinds (#677 contract registry).
/// Serialised by name; a kind is added, never renamed or removed.
/// <para>
/// <see cref="Cascade"/> is a leaf kind for subscription-cascade depth overflow, not a
/// wrapper: the outermost failure of a nested create still reports the leaf kind.
/// <c>NotFound</c> is not a kind; it is a host-level status for an unknown id.
/// </para>
/// </summary>
public enum DomainFailureKind {
    /// <summary>A required value was absent.</summary>
    Required = 0,

    /// <summary>A value fell outside its declared bounds.</summary>
    Range = 1,

    /// <summary>A value was shorter or longer than its declared length.</summary>
    Length = 2,

    /// <summary>A value did not match its declared pattern.</summary>
    Pattern = 3,

    /// <summary>A value was not one of the declared enum members.</summary>
    Enum = 4,

    /// <summary>A value duplicated an existing unique key.</summary>
    Unique = 5,

    /// <summary>An action ran outside its allowed stage.</summary>
    Stage = 6,

    /// <summary>A policy denied the action.</summary>
    Policy = 7,

    /// <summary>A link target did not satisfy the declared link.</summary>
    Link = 8,

    /// <summary>Checked arithmetic overflowed or divided by zero.</summary>
    Arithmetic = 9,

    /// <summary>An argument was missing or malformed.</summary>
    Argument = 10,

    /// <summary>Subscription-cascade depth overflow; a leaf kind, not a wrapper.</summary>
    Cascade = 11,

    /// <summary>Two writes conflicted on the same state.</summary>
    Conflict = 12,

    /// <summary>A constraint failed without a more specific kind.</summary>
    Constraint = 13,
}