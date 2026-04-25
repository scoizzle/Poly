using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

/// <summary>
/// Represents an effect that can occur as a result of an action. 
/// Effects can include publishing events, invoking external services, or modifying data.
/// </summary>
public abstract class Effect {
    public virtual IReadOnlyCollection<IDomainValue> RequiredParameters => [];
}