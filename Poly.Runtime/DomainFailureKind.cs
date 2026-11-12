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
    Required,
    Range,
    Length,
    Pattern,
    Enum,
    Unique,
    Stage,
    Policy,
    Link,
    Arithmetic,
    Argument,
    Cascade,
    Conflict,
    Constraint,
}