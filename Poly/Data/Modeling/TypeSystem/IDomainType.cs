namespace Poly.Data.Modeling.TypeSystem;

/// <summary>
/// Represents a type within a domain, which can be an entity or a value object.
/// </summary>
public interface IDomainType : IDomainObject {
    /// <summary>
    /// Gets the name of the type.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the properties of the type.
    /// </summary>
    public IReadOnlyCollection<Property> Properties { get; }
}