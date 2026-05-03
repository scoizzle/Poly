using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

/// <summary>
/// Represents an effect that can occur as a result of an action. 
/// Effects can include publishing events, invoking external services, or modifying data.
/// </summary>
/// <remarks>
/// Effects are designed to be extensible, allowing for a wide range of behaviors to be implemented
/// </remarks>
public abstract record Effect(Domain Domain) : DomainObject(Domain) {
}