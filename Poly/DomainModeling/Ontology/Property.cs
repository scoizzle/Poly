namespace Poly.DomainModeling.Ontology;

/// <summary>
/// A named piece of data belonging to a <see cref="DomainType"/> (Entity, ValueType, etc.).
/// </summary>
public sealed record Property(
    string Name,
    DomainTypeReference Type,
    IReadOnlyList<Constraint> Constraints
) : DomainMember(Name) {
    /// <summary>Property-level facets (column, json, pii, …).</summary>
    public IReadOnlyList<Facet> Facets { get; init; } = [];

    public sealed override IEnumerable<Node?> Children => [.. Constraints, .. Facets];
}