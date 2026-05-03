using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;


public enum RelationshipCardinality {
    OneToOne,
    OneToMany,
    ManyToOne,
    ManyToMany
}

public sealed partial record Relationship : Entity {
    public Relationship(Domain domain, string name, Entity source, Entity target, RelationshipCardinality cardinality, bool sourceOwnsTarget) : base(domain, name) {
        Source = source;
        Target = target;
        Cardinality = cardinality;
        SourceOwnsTarget = sourceOwnsTarget;
    }

    public Entity Source { get; private set; }

    public Entity Target { get; private set; }

    public RelationshipCardinality Cardinality { get; private set; }

    public bool SourceOwnsTarget { get; private set; }


    public sealed override IEnumerable<DomainMember> ChildObjects => [Source, Target, .. base.ChildObjects];
}