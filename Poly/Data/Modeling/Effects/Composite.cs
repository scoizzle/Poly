using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed partial record Composite(Domain Domain) : Effect(Domain) {
    private readonly List<Effect> _childEffects = [];

    public IReadOnlyCollection<Effect> ChildEffects => _childEffects.AsReadOnly();
    public override IEnumerable<DomainObject> ChildObjects => _childEffects;

    internal void AddEffect(Effect effect) {
        ArgumentNullException.ThrowIfNull(effect);
        _childEffects.Add(effect);
    }
}