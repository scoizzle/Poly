using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;


public enum RelationshipCardinality {
    OneToOne,
    OneToMany,
    ManyToOne,
    ManyToMany
}

public sealed class Relationship : Entity {
    public IDomainType Source { get; set; } = null!;
    public IDomainType Target { get; set; } = null!;
    public RelationshipCardinality Cardinality { get; set; } = RelationshipCardinality.OneToOne;
    public bool SourceOwnsTarget { get; set; }
}