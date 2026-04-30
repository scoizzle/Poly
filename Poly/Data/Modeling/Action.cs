using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
namespace Poly.Data.Modeling;

public sealed partial record Action : DomainObject {
    internal readonly List<Property> _parameters = [];
    internal readonly List<Effect> _effects = [];

    public Action(Domain domain, string name, Entity entity) : base(domain) {
        ArgumentNullException.ThrowIfNull(entity);
        Name = name;
        Entity = entity;
    }

    public Entity Entity { get; }

    public IReadOnlyCollection<Property> Parameters => _parameters.AsReadOnly();
    public IReadOnlyCollection<Effect> Effects => _effects.AsReadOnly();

    public sealed override IEnumerable<DomainObject> ChildObjects => [.. _parameters /*TODO: , .. _effects*/];
}