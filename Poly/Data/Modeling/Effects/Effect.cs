using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

/// <summary>
/// Represents an effect that can occur as a result of an action. 
/// Effects can include publishing events, invoking external services, or modifying data.
/// </summary>
/// <remarks>
/// Effects are designed to be extensible, allowing for a wide range of behaviors to be implemented
/// TODO: Consider adding a base class of type DomainObject to effects
/// </remarks>
public abstract class Effect {
    public abstract void Validate(Entity entity);
}