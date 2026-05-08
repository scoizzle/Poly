using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
namespace Poly.Data.Modeling;

public sealed partial record Action : DomainMember {
    internal readonly List<Property> _parameters = [];
    internal readonly List<Effect> _effects = [];
    internal readonly List<Policy> _policies = [];
    internal ActionTrigger _trigger = ActionTrigger.Default;

    public Action(Domain domain, string name, Entity entity) : base(domain, name) {
        ArgumentNullException.ThrowIfNull(entity);
        Entity = entity;
    }

    public Entity Entity { get; }

    public IReadOnlyCollection<Property> Parameters => _parameters.AsReadOnly();
    public IReadOnlyCollection<Effect> Effects => _effects.AsReadOnly();
    public IReadOnlyCollection<Policy> Policies => _policies.AsReadOnly();
    public ActionTrigger Trigger => _trigger;

    public sealed override IEnumerable<DomainMember> ChildObjects => [.. _parameters, .. _policies];
}