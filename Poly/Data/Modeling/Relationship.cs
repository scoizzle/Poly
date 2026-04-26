using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;


public enum RelationshipCardinality {
    OneToOne,
    OneToMany,
    ManyToOne,
    ManyToMany
}

public sealed class Relationship(Domain domain, string name) : Entity(domain, name) {
    private IDomainType _source = null!;
    private IDomainType _target = null!;
    private RelationshipCardinality _cardinality = RelationshipCardinality.OneToOne;
    private bool _sourceOwnsTarget;

    public IDomainType Source {
        get => _source;
        set {
            Domain.EvaluateRelationshipMutationPreconditions(this, value, _target, _cardinality, _sourceOwnsTarget);
            _source = value;
        }
    }

    public IDomainType Target {
        get => _target;
        set {
            Domain.EvaluateRelationshipMutationPreconditions(this, _source, value, _cardinality, _sourceOwnsTarget);
            _target = value;
        }
    }

    public RelationshipCardinality Cardinality {
        get => _cardinality;
        set {
            Domain.EvaluateRelationshipMutationPreconditions(this, _source, _target, value, _sourceOwnsTarget);
            _cardinality = value;
        }
    }

    public bool SourceOwnsTarget {
        get => _sourceOwnsTarget;
        set {
            Domain.EvaluateRelationshipMutationPreconditions(this, _source, _target, _cardinality, value);
            _sourceOwnsTarget = value;
        }
    }
}