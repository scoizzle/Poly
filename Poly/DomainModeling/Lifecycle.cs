namespace Poly.DomainModeling;

/// <summary>
/// Defines a finite state machine for entity lifecycle management.
/// States represent the meaningful phases an entity passes through;
/// transitions define the allowed movements between states and the commands that trigger them.
/// </summary>
/// <param name="States">All possible states in this lifecycle.</param>
/// <param name="Transitions">Allowed transitions between states.</param>
/// <param name="InitialState">The state name an entity starts in when created.</param>
/// <param name="TerminalStates">
/// States from which no outbound transitions are allowed.
/// Null or empty means all states are non-terminal (lifecycle can loop).
/// </param>
public sealed record Lifecycle(
    IReadOnlyList<LifecycleState> States,
    IReadOnlyList<StateTransition> Transitions,
    string InitialState,
    IReadOnlySet<string>? TerminalStates = null
);

/// <summary>
/// A named state within a <see cref="Lifecycle"/>.
/// </summary>
/// <param name="Name">Unique state name within the lifecycle.</param>
/// <param name="Description">Optional human-readable description of what this state means.</param>
public sealed record LifecycleState(
    string Name,
    string? Description = null
);

/// <summary>
/// A directed transition between two <see cref="LifecycleState"/> entries.
/// </summary>
/// <param name="Name">Unique transition name (e.g., "Approve", "Terminate").</param>
/// <param name="FromState">The source state name.</param>
/// <param name="ToState">The target state name.</param>
/// <param name="CommandName">
/// Optional command (mutation) name that triggers this transition.
/// When set, executing the named command is the only way to perform this transition.
/// </param>
public sealed record StateTransition(
    string Name,
    string FromState,
    string ToState,
    string? CommandName = null
);