using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed class Action {
    private readonly List<IDomainValue> _parameters = [];
    private readonly List<Effect> _effects = [];

    public required Entity Entity { get; init; }
    public required string Name { get; set; }

    public IReadOnlyCollection<IDomainValue> Parameters => _parameters;
    public IReadOnlyCollection<Effect> Effects => _effects;

    public void AddParameter(IDomainValue parameter) => _parameters.Add(parameter);
    public void AddEffect(Effect effect) => _effects.Add(effect);
}