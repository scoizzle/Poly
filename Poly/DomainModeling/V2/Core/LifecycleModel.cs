namespace Poly.DomainModeling.V2.Core;

/// <summary>
/// When the evaluation pipeline processes a Command invocation against a DomainType with a linked LifecycleModel,
/// it attempts to fire the Transition whose TriggerName equals the Command.Name (case-sensitive). If the matched
/// Transition has IsExternallyApproved=true, the pipeline emits a pending-approval record instead of advancing state immediately.
/// </summary>
public sealed record LifecycleModel {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public IReadOnlyList<LifecycleState> States { get; }
    public IReadOnlyList<Transition> Transitions { get; }

    public LifecycleModel(
        SemanticId semanticId,
        string name,
        IEnumerable<LifecycleState> states,
        IEnumerable<Transition> transitions)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("LifecycleModel name cannot be null, empty, or whitespace.", nameof(name))
            : name;

        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(transitions);

        var stateList = states.ToArray();
        var transitionList = transitions.ToArray();

        if (stateList.Count(state => state.IsInitial) != 1) {
            throw new ArgumentException("LifecycleModel must contain exactly one initial state.", nameof(states));
        }

        var stateIds = stateList.Select(state => state.SemanticId).ToHashSet();
        foreach (var transition in transitionList) {
            if (!stateIds.Contains(transition.FromStateId) || !stateIds.Contains(transition.ToStateId)) {
                throw new ArgumentException("All transitions must reference states in the same LifecycleModel.", nameof(transitions));
            }
        }

        States = stateList;
        Transitions = transitionList;
    }
}

public sealed record LifecycleState {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public bool IsInitial { get; }
    public bool IsTerminal { get; }

    public LifecycleState(SemanticId semanticId, string name, bool isInitial, bool isTerminal)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("LifecycleState name cannot be null, empty, or whitespace.", nameof(name))
            : name;

        if (isInitial && isTerminal) {
            throw new ArgumentException("LifecycleState cannot be both initial and terminal.", nameof(isTerminal));
        }

        IsInitial = isInitial;
        IsTerminal = isTerminal;
    }
}

public sealed record Transition(
    SemanticId SemanticId,
    SemanticId FromStateId,
    SemanticId ToStateId,
    string TriggerName,
    SemanticId? TriggerCommandId = null,
    bool IsExternallyApproved = false,
    string? GuardExpression = null) {
    public Transition : this
    {
        ArgumentNullException.ThrowIfNull(SemanticId);
        ArgumentNullException.ThrowIfNull(FromStateId);
        ArgumentNullException.ThrowIfNull(ToStateId);

        if (string.IsNullOrWhiteSpace(TriggerName))
        {
            throw new ArgumentException("Transition trigger name cannot be null, empty, or whitespace.", nameof(TriggerName));
}
    }
}