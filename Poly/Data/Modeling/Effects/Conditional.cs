using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed record Conditional(Domain Domain) : Effect(Domain) {
    private readonly List<Effect> _childEffects = [];

    public required Node Condition { get; init; }

    public IReadOnlyCollection<Effect> ChildEffects => _childEffects.AsReadOnly();
    public override IEnumerable<DomainObject> ChildObjects => _childEffects;

    public void AddEffect(Effect effect) {
        ArgumentNullException.ThrowIfNull(effect);
        _childEffects.Add(effect);
    }
}