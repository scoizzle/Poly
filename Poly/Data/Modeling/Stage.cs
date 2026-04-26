using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed class Stage : IDomainObject {
    private readonly List<Policy> _policies = [];
    private readonly List<Action> _actions = [];

    public required string Name { get; set; }
    public required Domain Domain { get; init; }
    public IReadOnlyCollection<Policy> Policies => _policies.AsReadOnly();
    public IReadOnlyCollection<Action> Actions => _actions.AsReadOnly();
    public Stage? Parent { get; init; }
    public IReadOnlyCollection<Stage> Children { get; init; } = [];

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