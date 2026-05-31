namespace Poly.DomainModeling;

/// <summary>
/// A named piece of data belonging to a <see cref="DomainType"/> (Entity, Event, ValueType, etc.).
/// </summary>
public sealed record Property(
    string Name,
    DomainTypeReference Type,
    IReadOnlyList<Constraint> Constraints
) : DomainMember(Name) {
    public sealed override IEnumerable<Node?> Children => [.. Constraints];
}