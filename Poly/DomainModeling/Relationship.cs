namespace Poly.DomainModeling;

/// <summary>
/// Defines the cardinality options for relationships between entities.
/// </summary>
public enum RelationshipCardinality {
    OneToOne,
    OneToMany,
    ManyToOne,
    ManyToMany
}

/// <summary>
/// Represents a named relationship between two entity types.
/// 
/// Relationships are first-class members of a <see cref="Domain"/> and can carry their own properties.
/// They are distinct from owned structures (which are typically modeled via owned value types or composition).
/// </summary>
public sealed record Relationship(
    string Name,
    DomainTypeReference Source,
    DomainTypeReference Target,
    RelationshipCardinality Cardinality,
    IReadOnlyList<Property> Properties
) : DomainMember(Name) {
    public IReadOnlyList<Stage> Stages { get; init; } = [];
    public IReadOnlyList<Policy> Policies { get; init; } = [];
    public sealed override IEnumerable<Node?> Children => [Source, Target, .. Properties, .. Stages, .. Policies];
}