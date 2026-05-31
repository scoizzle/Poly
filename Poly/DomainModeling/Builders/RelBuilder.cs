using Poly.DomainModeling.Builders;

namespace Poly.DomainModeling;

public sealed class RelBuilder(DomainBuilder domainBuilder, RelationshipCardinality cardinality, string name, string sourceEntityName, string targetEntityName) : DomainMemberBuilder(domainBuilder) {
    public string Name { get; private set; } = Guard.ThrowIfNullOrEmpty(name);
    public string SourceEntityName { get; private set; } = Guard.ThrowIfNullOrEmpty(sourceEntityName);
    public string TargetEntityName { get; private set; } = Guard.ThrowIfNullOrEmpty(targetEntityName);
    public RelationshipCardinality Cardinality { get; private set; } = cardinality;

    public RelBuilder Source(string entityName) {
        SourceEntityName = Guard.ThrowIfNullOrEmpty(entityName);
        return this;
    }

    public RelBuilder Target(string entityName) {
        TargetEntityName = Guard.ThrowIfNullOrEmpty(entityName);
        return this;
    }

    public RelBuilder WithCardinality(RelationshipCardinality cardinality) {
        Cardinality = cardinality;
        return this;
    }
}