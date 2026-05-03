namespace Poly.Data.Modeling.TypeSystem;

/// <summary>
/// Represents a value within a domain, which is an instance of a domain type.
/// </summary>
public abstract record DomainValue(Domain Domain, string Name, DomainType Type) : DomainMember(Domain, Name);