using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed record Action : DomainObject {
    private readonly List<IDomainValue> _parameters = [];
    private readonly List<Effect> _effects = [];

    public Action(Domain domain, string name, Entity entity) : base(domain) {
        ArgumentNullException.ThrowIfNull(entity);
        Name = name;
        Entity = entity;
    }

    public Entity Entity { get; }
    public string Name { get; set; }

    public IReadOnlyCollection<IDomainValue> Parameters => _parameters.AsReadOnly();
    public IReadOnlyCollection<Effect> Effects => _effects.AsReadOnly();

    public void AddParameter(IDomainValue parameter) {
        parameter.ThrowIfNullOrMismatchedDomain(Domain);

        if (parameter is Property property && _parameters.OfType<Property>().Any(existing => string.Equals(existing.Name, property.Name, StringComparison.Ordinal))) {
            throw new InvalidOperationException($"Parameter '{property.Name}' already exists on action '{Name}'.");
        }

        _parameters.Add(parameter);
    }

    public void AddEffect(Effect effect) {
        ArgumentNullException.ThrowIfNull(effect);

        effect.Validate(Entity);

        _effects.Add(effect);
    }

    public bool RemoveEffect(Effect effect) {
        ArgumentNullException.ThrowIfNull(effect);
        return _effects.Remove(effect);
    }
}