namespace Poly.Data.Modeling.TypeSystem;

/// <summary>
/// Represents a value within a domain, which is an instance of a domain type.
/// </summary>
public interface IDomainValue {
    /// <summary>
    /// Gets the type of the value.
    /// </summary>
    public IDomainType Type { get; }
}