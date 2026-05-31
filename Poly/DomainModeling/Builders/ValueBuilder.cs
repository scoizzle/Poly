using Poly.DomainModeling.Builders;

namespace Poly.DomainModeling;

/// <summary>
/// Mutable builder for ValueTypes / owned documents.
/// </summary>
public sealed class ValueBuilder : DomainMemberBuilder {
    private readonly string _name;
    private readonly List<Property> _properties = new();

    internal ValueBuilder(DomainBuilder domainBuilder, string name) : base(domainBuilder) {
        _name = Guard.ThrowIfNullOrEmpty(name);
    }

    public ValueBuilder Property(string name, string typeName) {
        _properties.Add(new Property(
            Guard.ThrowIfNullOrEmpty(name),
            new DomainTypeReference(Guard.ThrowIfNullOrEmpty(typeName)),
            []
        ));
        return this;
    }

    internal new ValueType Build() {
        return new ValueType(_name, _properties, []);
    }
}