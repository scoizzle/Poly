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
    public IReadOnlyCollection<Relationship> Relationships => _relationships;

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

        _relationships.Add(relationship);
    }
}