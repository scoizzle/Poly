using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed partial record Stage : DomainMember {
    private readonly List<Policy> _policies = [];
    private readonly List<Action> _actions = [];
    private readonly List<Stage> _childStages = [];
    private Entity? _ownerEntity;

    public Stage(Domain domain, string name) : base(domain, name) {
    }

    public IReadOnlyCollection<Policy> Policies => _policies.AsReadOnly();
    public IReadOnlyCollection<Action> Actions => _actions.AsReadOnly();
    internal Entity? OwnerEntity => _ownerEntity;
    public Stage? Parent { get; init; }
    public IReadOnlyCollection<Stage> ChildStages => _childStages.AsReadOnly();
    public sealed override IEnumerable<DomainMember> ChildObjects => [.. _policies, .. _actions, .. _childStages];

    internal void AttachToEntity(Entity ownerEntity) {
        ArgumentNullException.ThrowIfNull(ownerEntity);
        if (_ownerEntity is not null && !ReferenceEquals(_ownerEntity, ownerEntity)) {
            throw new InvalidOperationException(
                $"Stage '{Name}' is already attached to entity '{_ownerEntity.Name}' and cannot be attached to '{ownerEntity.Name}'.");
        }

        _ownerEntity = ownerEntity;
    }

    internal void DetachFromEntity(Entity ownerEntity) {
        ArgumentNullException.ThrowIfNull(ownerEntity);
        if (!ReferenceEquals(_ownerEntity, ownerEntity)) {
            throw new InvalidOperationException(
                $"Stage '{Name}' is not attached to entity '{ownerEntity.Name}'.");
        }

        _ownerEntity = null;
    }
}