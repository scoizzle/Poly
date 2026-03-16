namespace Poly.DomainModeling.Builders;

/// <summary>
/// Fluent builder for constructing a <see cref="Lifecycle"/> state machine.
/// </summary>
public sealed class LifecycleBuilder {
    private readonly List<LifecycleState> _states = [];
    private readonly List<StateTransition> _transitions = [];
    private string? _initialState;
    private readonly HashSet<string> _terminalStates = [];

    /// <summary>
    /// Adds a state to the lifecycle.
    /// </summary>
    public LifecycleBuilder AddState(string name, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        _states.Add(new LifecycleState(name, description));
        return this;
    }

    /// <summary>
    /// Adds a transition between two states.
    /// </summary>
    /// <param name="name">Transition name (e.g., "Approve").</param>
    /// <param name="fromState">Source state name.</param>
    /// <param name="toState">Target state name.</param>
    /// <param name="commandName">Optional command that triggers this transition.</param>
    public LifecycleBuilder AddTransition(string name, string fromState, string toState, string? commandName = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(fromState);
        ArgumentNullException.ThrowIfNull(toState);
        _transitions.Add(new StateTransition(name, fromState, toState, commandName));
        return this;
    }

    /// <summary>
    /// Sets the initial state for newly created entities.
    /// </summary>
    public LifecycleBuilder SetInitialState(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _initialState = name;
        return this;
    }

    /// <summary>
    /// Marks a state as terminal (no outbound transitions allowed).
    /// </summary>
    public LifecycleBuilder AddTerminalState(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _terminalStates.Add(name);
        return this;
    }

    /// <summary>
    /// Builds the lifecycle definition.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no initial state is set or no states are defined.</exception>
    public Lifecycle Build()
    {
        if (_states.Count == 0)
            throw new InvalidOperationException("A lifecycle must have at least one state.");

        if (_initialState is null)
            throw new InvalidOperationException("A lifecycle must have an initial state. Call SetInitialState().");

        if (!_states.Any(s => s.Name == _initialState))
            throw new InvalidOperationException($"Initial state '{_initialState}' is not defined. Add it with AddState().");

        foreach (var terminal in _terminalStates) {
            if (!_states.Any(s => s.Name == terminal))
                throw new InvalidOperationException($"Terminal state '{terminal}' is not defined. Add it with AddState().");
        }

        foreach (var transition in _transitions) {
            if (!_states.Any(s => s.Name == transition.FromState))
                throw new InvalidOperationException($"Transition '{transition.Name}' references undefined source state '{transition.FromState}'.");
            if (!_states.Any(s => s.Name == transition.ToState))
                throw new InvalidOperationException($"Transition '{transition.Name}' references undefined target state '{transition.ToState}'.");
        }

        return new Lifecycle(
            _states,
            _transitions,
            _initialState,
            _terminalStates.Count > 0 ? _terminalStates : null
        );
    }
}