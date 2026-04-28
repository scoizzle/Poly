using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed partial record Domain : DomainObject {
    private readonly Lock _mutationLock = new();
    private readonly List<DomainType> _types = new();
    private readonly List<Relationship> _relationships = new();

    public Domain(string name) {
        Name = Guard.ThrowIfNullOrEmpty(name);
    }

    public string Name { get; private set; } = string.Empty;
    public IReadOnlyCollection<DomainType> Types => _types.AsReadOnly();
    public IReadOnlyCollection<Relationship> Relationships => _relationships.AsReadOnly();
    public override IEnumerable<Node?> Children => [.. _types, .. _relationships];

    internal void EvaluateRelationshipMutationPreconditions(
        Relationship relationship,
        IDomainType? source,
        IDomainType? target,
        RelationshipCardinality cardinality,
        bool sourceOwnsTarget) {
        ArgumentNullException.ThrowIfNull(relationship);

        var isRegistered = _relationships.Contains(relationship);

        if (source is null || target is null) {
            if (isRegistered) {
                throw new InvalidOperationException(
                    $"Relationship '{relationship.Name}' is already registered and must keep both source and target defined.");
            }

            return;
        }

        if (!ReferenceEquals(source.Domain, this) || !ReferenceEquals(target.Domain, this)) {
            throw new InvalidOperationException("Relationship source and target entities must belong to the same domain.");
        }

        if (sourceOwnsTarget) {
            if (source is not Entity)
                throw new InvalidOperationException("Ownership relationship source must be an entity.");

            if (target is not Entity)
                throw new InvalidOperationException("Ownership relationship target must be an entity.");

            if (cardinality is RelationshipCardinality.ManyToOne or RelationshipCardinality.ManyToMany)
                throw new InvalidOperationException("Ownership relationships must have one-to-one or one-to-many cardinality.");

            if (_relationships.Any(existing =>
                    !ReferenceEquals(existing, relationship)
                    && existing.SourceOwnsTarget
                    && ReferenceEquals(existing.Target, target))) {
                throw new InvalidOperationException($"Target '{target.Name}' already has an ownership relationship.");
            }
        }

        if (isRegistered) {
            var attachedOwners = Types
                .OfType<Entity>()
                .Where(entity => entity.Relationships.Contains(relationship))
                .ToArray();

            if (attachedOwners.Any(owner => !ReferenceEquals(owner, source))) {
                throw new InvalidOperationException(
                    $"Relationship '{relationship.Name}' source must remain aligned with attached entity relationships.");
            }
        }
    }

    public Mutation CreateMutation(DomainModelAnalyzer? analyzer = null) {
        return new Mutation(this, analyzer ?? new DomainModelAnalyzer());
    }
}