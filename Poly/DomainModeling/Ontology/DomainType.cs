namespace Poly.DomainModeling.Ontology;

/// <summary>
/// A reference to a domain type by name (used before full resolution by analyzers).
/// </summary>
public sealed record DomainTypeReference(string TypeName) : DomainObject;

/// <summary>
/// Base type for all named types in the domain model (Entity, ValueType, PrimitiveType, EnumType).
/// </summary>
public abstract record DomainType(
    string Name,
    IReadOnlyList<Property> Properties,
    IReadOnlyList<Constraint> Constraints
) : DomainMember(Name) {
    /// <summary>Type-level facets (table, schema, …).</summary>
    public IReadOnlyList<Facet> Facets { get; init; } = [];

    public override IEnumerable<Node?> Children => [.. Properties, .. Constraints, .. Facets];
}