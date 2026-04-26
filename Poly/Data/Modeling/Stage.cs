using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed class Stage : IDomainObject {
    private readonly List<Policy> _policies = [];
    private readonly List<Action> _actions = [];
    private Entity? _ownerEntity;

    public required string Name { get; set; }
    public required Domain Domain { get; init; }
    public IReadOnlyCollection<Policy> Policies => _policies.AsReadOnly();
    public IReadOnlyCollection<Action> Actions => _actions.AsReadOnly();
    public Stage? Parent { get; init; }
    public IReadOnlyCollection<Stage> Children { get; init; } = [];

    internal void AttachToEntity(Entity ownerEntity) {
        ArgumentNullException.ThrowIfNull(ownerEntity);

        ownerEntity.ThrowIfMismatchedDomain(Domain);

        if (_ownerEntity is not null && !ReferenceEquals(_ownerEntity, ownerEntity)) {
            throw new InvalidOperationException(
                $"Stage '{Name}' is already attached to entity '{_ownerEntity.Name}' and cannot be attached to '{ownerEntity.Name}'.");
        }

        foreach (var action in _actions) {
            if (!ReferenceEquals(action.Entity, ownerEntity)) {
                throw new InvalidOperationException(
                    $"Action '{action.Name}' on stage '{Name}' must belong to entity '{ownerEntity.Name}'.");
            }
        }

        _ownerEntity = ownerEntity;
    }

    public void AddPolicy(Policy policy) {
        policy.ThrowIfNullOrMismatchedDomain(Domain);

        if (_policies.Any(existing => string.Equals(existing.Name, policy.Name, StringComparison.Ordinal))) {
            throw new InvalidOperationException($"Policy '{policy.Name}' already exists on stage '{Name}'.");
        }

        _policies.Add(policy);
    }

    public bool RemovePolicy(Policy policy) {
        policy.ThrowIfNullOrMismatchedDomain(Domain);
        return _policies.Remove(policy);
    }

    public void AddAction(Action action) {
        action.ThrowIfNullOrMismatchedDomain(Domain);

        if (_ownerEntity is not null && !ReferenceEquals(action.Entity, _ownerEntity)) {
            throw new InvalidOperationException(
                $"Action '{action.Name}' on stage '{Name}' must belong to entity '{_ownerEntity.Name}'.");
        }

        if (_actions.Any(existing => string.Equals(existing.Name, action.Name, StringComparison.Ordinal))) {
            throw new InvalidOperationException($"Action '{action.Name}' already exists on stage '{Name}'.");
        }

        _actions.Add(action);
    }

    public bool RemoveAction(Action action) {
        action.ThrowIfNullOrMismatchedDomain(Domain);
        return _actions.Remove(action);
    }

    public IEnumerable<Policy> GetEffectivePolicies() {
        var policies = Policies.ToDictionary(policy => policy.Name, StringComparer.Ordinal);

        for (var current = Parent; current != null; current = current.Parent) {
            foreach (var policy in current.Policies) {
                _ = policies.TryAdd(policy.Name, policy);
            }
        }

        return policies.Values;
    }

    public IEnumerable<Action> GetEffectiveActions() {
        var actions = Actions.ToDictionary(e => e.Name);

        for (var current = Parent; current != null; current = current.Parent) {
            foreach (var action in current.Actions) {
                _ = actions.TryAdd(action.Name, action);
            }
        }

        return actions.Values;
    }
}