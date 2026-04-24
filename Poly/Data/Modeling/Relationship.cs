using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;


public enum RelationshipCardinality {
    OneToOne,
    OneToMany,
    ManyToOne,
    ManyToMany
}

public sealed class Relationship {
    public required Domain Domain { get; init; }
    public string Name { get; set; } = string.Empty;
    public IDomainType Source { get; set; } = null!;
    public IDomainType Target { get; set; } = null!;
    public RelationshipCardinality Cardinality { get; set; } = RelationshipCardinality.OneToOne;
    public bool IsOwnership { get; set; }
    public string? InverseRelationshipName { get; set; }
}