using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
namespace Poly.Data.Modeling;

public sealed partial record Action : DomainObject {
    private readonly List<Property> _parameters = [];
    private readonly List<Effect> _effects = [];

    public Action(Domain domain, string name, Entity entity) : base(domain) {
        ArgumentNullException.ThrowIfNull(entity);
        Name = name;
        Entity = entity;
    }

    public Entity Entity { get; }
    public string Name { get; internal set; }

    public IReadOnlyCollection<Property> Parameters => _parameters.AsReadOnly();
    public IReadOnlyCollection<Effect> Effects => _effects.AsReadOnly();

    private void AddParameter(Property parameter) {
        parameter.ThrowIfNullOrMismatchedDomain(Domain);

        if (_parameters.Any(existing => string.Equals(existing.Name, parameter.Name, StringComparison.Ordinal))) {
            throw new InvalidOperationException($"Parameter '{parameter.Name}' already exists on action '{Name}'.");
        }

        _parameters.Add(parameter);
    }

    private bool RemoveParameter(Property parameter) {
        parameter.ThrowIfNullOrMismatchedDomain(Domain);
        return _parameters.Remove(parameter);
    }

    private void AddEffect(Effect effect) {
        ArgumentNullException.ThrowIfNull(effect);

        effect.Validate(Entity);

        _effects.Add(effect);
    }

    private bool RemoveEffect(Effect effect) {
        ArgumentNullException.ThrowIfNull(effect);
        return _effects.Remove(effect);
    }
}