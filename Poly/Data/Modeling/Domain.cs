using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed class Domain {
    private readonly Dictionary<string, IDomainType> _types = new();
    private readonly List<Relationship> _relationships = new();

    private Domain(ICollection<IDomainType> entities, ICollection<Relationship> relationships) {
        foreach (var entity in entities)
            AddType(entity);

        foreach (var relationship in relationships)
            AddRelationship(relationship);
    }

    public required string Name { get; set; }
    public IReadOnlyCollection<IDomainType> Types => _types.Values;
    public IReadOnlyCollection<Relationship> Relationships => _relationships.AsReadOnly();

    public void AddType(IDomainType type) {
        ArgumentNullException.ThrowIfNull(type);

        if (type.Domain != this)
            throw new InvalidOperationException("Entity domain must match parent domain.");

        if (!_types.TryAdd(type.Name, type))
            throw new InvalidOperationException($"An entity with the name '{type.Name}' already exists in the domain.");
    }

    public void AddRelationship(Relationship relationship) {
        ArgumentNullException.ThrowIfNull(relationship);

        if (relationship.Domain != this)
            throw new InvalidOperationException("Relationship domain must match parent domain.");

        if (relationship.Source.Domain != this || relationship.Target.Domain != this)
            throw new InvalidOperationException("Relationship source and target entities must belong to the same domain.");

        if (_relationships.Any(existing => string.Equals(existing.Name, relationship.Name, StringComparison.Ordinal)))
            throw new InvalidOperationException($"A relationship with the name '{relationship.Name}' already exists in the domain.");

        if (relationship.SourceOwnsTarget) {
            if (relationship.Source is not Entity)
                throw new InvalidOperationException("Ownership relationship source must be an entity.");

            if (relationship.Target is not Entity)
                throw new InvalidOperationException("Ownership relationship target must be an entity.");

            if (relationship.Cardinality is RelationshipCardinality.ManyToOne or RelationshipCardinality.ManyToMany)
                throw new InvalidOperationException("Ownership relationships must have one-to-one or one-to-many cardinality.");

            if (_relationships.Any(existing => existing.SourceOwnsTarget && ReferenceEquals(existing.Target, relationship.Target)))
                throw new InvalidOperationException($"Target '{relationship.Target.Name}' already has an ownership relationship.");
        }

        _relationships.Add(relationship);
    }
}