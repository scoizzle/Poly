using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed record Composite(Domain Domain) : Effect(Domain) {
    private readonly List<Effect> _childEffects = [];

    public IReadOnlyCollection<Effect> ChildEffects => _childEffects.AsReadOnly();

    public void AddEffect(Effect effect) {
        ArgumentNullException.ThrowIfNull(effect);
        _childEffects.Add(effect);
    }
}