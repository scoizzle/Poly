namespace Poly.DomainModeling.V2.Core;

public sealed record LifecycleState {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public bool IsInitial { get; }
    public bool IsTerminal { get; }

    public LifecycleState(SemanticId semanticId, string name, bool isInitial, bool isTerminal)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(name));
        }

        if (isInitial && isTerminal) {
            throw new ArgumentException("A state cannot be both initial and terminal.", nameof(isTerminal));
        }

        Name = name;
        IsInitial = isInitial;
        IsTerminal = isTerminal;
    }
}

public sealed record Transition {
    public SemanticId SemanticId { get; }
    public SemanticId FromStateId { get; }
    public SemanticId ToStateId { get; }
    public string TriggerName { get; }
    public SemanticId? TriggerCommandId { get; }
    public bool IsExternallyApproved { get; }
    public string? GuardExpression { get; }

    public Transition(
        SemanticId semanticId,
        SemanticId fromStateId,
        SemanticId toStateId,
        string triggerName,
        SemanticId? triggerCommandId = null,
        bool isExternallyApproved = false,
        string? guardExpression = null)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        FromStateId = fromStateId ?? throw new ArgumentNullException(nameof(fromStateId));
        ToStateId = toStateId ?? throw new ArgumentNullException(nameof(toStateId));
        if (string.IsNullOrWhiteSpace(triggerName)) {
            throw new ArgumentException("TriggerName cannot be null or whitespace.", nameof(triggerName));
        }

        TriggerName = triggerName;
        TriggerCommandId = triggerCommandId;
        IsExternallyApproved = isExternallyApproved;
        GuardExpression = guardExpression;
    }
}

/// <summary>
/// When the evaluation pipeline processes a Command invocation against a DomainType with a linked LifecycleModel,
/// it attempts to fire the Transition whose TriggerName equals the Command.Name (case-sensitive).
/// If the matched Transition has IsExternallyApproved=true, the pipeline emits a pending-approval record
/// instead of advancing state immediately.
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
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(name));
        }

        Name = name;

        var resolvedStates = (states ?? throw new ArgumentNullException(nameof(states))).ToArray();
        var resolvedTransitions = (transitions ?? throw new ArgumentNullException(nameof(transitions))).ToArray();

        var initialCount = resolvedStates.Count(s => s.IsInitial);
        if (initialCount != 1) {
            throw new ArgumentException("LifecycleModel must have exactly one initial state.", nameof(states));
        }

        var stateIds = resolvedStates.Select(s => s.SemanticId).ToHashSet();
        foreach (var transition in resolvedTransitions) {
            if (!stateIds.Contains(transition.FromStateId) || !stateIds.Contains(transition.ToStateId)) {
                throw new ArgumentException("Transition state references must exist in LifecycleModel states.", nameof(transitions));
            }
        }

        States = resolvedStates;
        Transitions = resolvedTransitions;
    }
}
namespace Poly.DomainModeling.V2.Core;

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
            throw new ArgumentException("A state cannot be both initial and terminal.", nameof(isTerminal));
        }

        IsInitial = isInitial;
        IsTerminal = isTerminal;
    }
}

public sealed record Transition {
    public SemanticId SemanticId { get; }
    public SemanticId FromStateId { get; }
    public SemanticId ToStateId { get; }
    public string TriggerName { get; }
    public SemanticId? TriggerCommandId { get; }
    public bool IsExternallyApproved { get; }
    public string? GuardExpression { get; }

    public Transition(
        SemanticId semanticId,
        SemanticId fromStateId,
        SemanticId toStateId,
        string triggerName,
        SemanticId? triggerCommandId = null,
        bool isExternallyApproved = false,
        string? guardExpression = null)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        FromStateId = fromStateId ?? throw new ArgumentNullException(nameof(fromStateId));
        ToStateId = toStateId ?? throw new ArgumentNullException(nameof(toStateId));
        TriggerName = string.IsNullOrWhiteSpace(triggerName)
            ? throw new ArgumentException("Transition trigger name cannot be null, empty, or whitespace.", nameof(triggerName))
            : triggerName;
        TriggerCommandId = triggerCommandId;
        IsExternallyApproved = isExternallyApproved;
        GuardExpression = guardExpression;
    }
}

/// <summary>
/// When the evaluation pipeline processes a Command invocation against a DomainType with a linked LifecycleModel,
/// it attempts to fire the Transition whose TriggerName equals the Command.Name (case-sensitive).
/// If the matched Transition has IsExternallyApproved=true, the pipeline emits a pending-approval record instead of advancing state immediately.
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

        var stateArray = states.ToArray();
        var transitionArray = transitions.ToArray();

        var initialStateCount = stateArray.Count(s => s.IsInitial);
        if (initialStateCount != 1) {
            throw new ArgumentException("LifecycleModel must contain exactly one initial state.", nameof(states));
        }

        var stateIds = stateArray.Select(s => s.SemanticId).ToHashSet();
        foreach (var transition in transitionArray) {
            if (!stateIds.Contains(transition.FromStateId) || !stateIds.Contains(transition.ToStateId)) {
                throw new ArgumentException("Transition references a state ID that is not present in this LifecycleModel.", nameof(transitions));
            }
        }

        States = stateArray;
        Transitions = transitionArray;
    }
}