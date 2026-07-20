namespace Poly.DomainModeling;

/// <summary>
/// A first-class enum type with a fixed set of named members.
/// Properties reference an <see cref="EnumType"/> by name (not by string + constraint).
/// Expressions compare with bare identifiers: <c>Status is Active</c>.
/// </summary>
public sealed record EnumType(
    string Name,
    IReadOnlyList<string> MemberNames,
    IReadOnlyList<Constraint> Constraints
) : DomainType(Name, [], Constraints);