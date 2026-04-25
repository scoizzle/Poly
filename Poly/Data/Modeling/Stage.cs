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
        ArgumentNullException.ThrowIfNull(policy);
        _policies.Add(policy);
    }

    public bool RemovePolicy(Policy policy) {
        ArgumentNullException.ThrowIfNull(policy);
        return _policies.Remove(policy);
    }

    public void AddAction(Action action) {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Add(action);
    }

    public bool RemoveAction(Action action) {
        ArgumentNullException.ThrowIfNull(action);
        return _actions.Remove(action);
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