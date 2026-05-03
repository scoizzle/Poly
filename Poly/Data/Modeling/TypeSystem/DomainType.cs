namespace Poly.Data.Modeling.TypeSystem;

/// <summary>
/// Represents a type within a domain, which can be an entity or a value object.
/// </summary>
public abstract record DomainType : DomainMember {
    protected readonly List<Property> _properties = [];
    public DomainType(Domain domain, string name, params IEnumerable<Property> properties) : base(domain, name) {
        _properties.AddRange(properties);
    }

    /// <summary>
    /// Gets the properties of the type.
    /// </summary>
    public IReadOnlyCollection<Property> Properties => _properties;
}