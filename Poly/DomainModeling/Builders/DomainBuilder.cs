namespace Poly.DomainModeling.Builders;

public abstract class DomainMemberBuilder {
    protected readonly DomainBuilder _domainBuilder;

    protected DomainMemberBuilder(DomainBuilder domainBuilder) {
        _domainBuilder = domainBuilder;
    }

    protected DomainMemberBuilder(DomainMemberBuilder domainMemberBuilder)
        : this(domainMemberBuilder._domainBuilder) { }

    public EntityBuilder Entity(string name) => _domainBuilder.Entity(name);

    public RelBuilder Relationship(string name, string sourceEntityName, string targetEntityName, RelationshipCardinality cardinality) =>
        _domainBuilder.Relationship(name, sourceEntityName, targetEntityName, cardinality);

    public Domain Build() => _domainBuilder.Build();
}

// Note: Old prototype PropBuilder / EvBuilder / EventPropBuilder code has been removed
// as part of builder infrastructure cleanup. A cleaner property/event builder surface
// can be reintroduced later if needed.

public sealed class DomainBuilder {
    private readonly Dictionary<string, ValueBuilder> _valueTypeBuilders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EntityBuilder> _entityBuilders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RelBuilder> _relationshipBuilders = new(StringComparer.Ordinal);

    private readonly List<PrimitiveType> _primitives = new();

    public string Name { get; private set; } = string.Empty;

    public DomainBuilder() { }

    public DomainBuilder(string name) {
        Name = Guard.ThrowIfNullOrEmpty(name);
    }

    public DomainBuilder Named(string name) {
        Name = Guard.ThrowIfNullOrEmpty(name);
        return this;
    }

    public EntityBuilder Entity(string name) {
        if (!_entityBuilders.TryGetValue(name, out var builder)) {
            builder = new EntityBuilder(this, name);
            _entityBuilders.Add(name, builder);
        }
        return builder;
    }

    public DomainBuilder Entity(string name, Action<EntityBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Entity(name));
        return this;
    }

    public DomainBuilder PrimitiveType(string name, TypeCategory typeCategory) {
        _primitives.Add(new PrimitiveType(Guard.ThrowIfNullOrEmpty(name), typeCategory, []));
        return this;
    }

    /// <summary>
    /// Alias for ValueType, matching the style in the original Ugh sketch.
    /// </summary>
    public DomainBuilder Type(string name, Action<ValueBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var vb = ValueType(Guard.ThrowIfNullOrEmpty(name));
        configure(vb);
        return this;
    }

    public ValueBuilder ValueType(string name) {
        if (!_valueTypeBuilders.TryGetValue(name, out var builder)) {
            builder = new ValueBuilder(this, Guard.ThrowIfNullOrEmpty(name));
            _valueTypeBuilders.Add(name, builder);
        }
        return builder;
    }

    public RelBuilder Relationship(string name, string sourceEntityName, string targetEntityName, RelationshipCardinality cardinality) {
        if (!_relationshipBuilders.TryGetValue(name, out var builder)) {
            builder = new RelBuilder(this, cardinality, name, sourceEntityName, targetEntityName);
            _relationshipBuilders.Add(name, builder);
            return builder;
        }

        builder.Source(sourceEntityName).Target(targetEntityName).WithCardinality(cardinality);
        return builder;
    }

    public DomainBuilder Relationship(
        string name,
        string sourceEntityName,
        string targetEntityName,
        RelationshipCardinality cardinality,
        Action<RelBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Relationship(name, sourceEntityName, targetEntityName, cardinality));
        return this;
    }

    public Domain Build() {
        var entities = _entityBuilders.Values
            .Select(b => b.Build())
            .ToList();

        var valueTypes = _valueTypeBuilders.Values
            .Select(b => b.Build())
            .ToList();

        var relationships = _relationshipBuilders.Values
            .Select(b => new Relationship(
                b.Name,
                new DomainTypeReference(b.SourceEntityName),
                new DomainTypeReference(b.TargetEntityName),
                b.Cardinality,
                []))
            .ToList();

        var allTypes = new List<DomainType>();
        allTypes.AddRange(_primitives);
        allTypes.AddRange(valueTypes);
        allTypes.AddRange(entities);

        return new Domain(Name, allTypes, relationships);
    }

    // The original "Ugh" sketch (aspirational DSL) has been removed.
    // Real builder usage is now demonstrated in PersonLifecycleViaBuilders.cs
}