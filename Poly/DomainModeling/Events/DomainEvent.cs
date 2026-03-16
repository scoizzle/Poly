using Poly.DomainModeling.TypeExpressions;

namespace Poly.DomainModeling.Events;

/// <summary>
/// Defines a domain event — a named, immutable description of something that happened.
/// Events are produced by commands (mutations) and describe state changes in the past tense
/// (e.g., "PersonCreated", "EmploymentStarted", "AssignmentCompleted").
/// </summary>
/// <param name="Name">The event name (past-tense by convention, e.g., "PersonCreated").</param>
/// <param name="Description">Optional human-readable description.</param>
/// <param name="Properties">The data carried by this event.</param>
public sealed record DomainEvent(
    string Name,
    string? Description = null,
    IEnumerable<DomainEventProperty>? Properties = null
);

/// <summary>
/// A property carried by a <see cref="DomainEvent"/>.
/// </summary>
/// <param name="Name">Property name.</param>
/// <param name="Type">The type expression for this property's value.</param>
/// <param name="Description">Optional description.</param>
public sealed record DomainEventProperty(
    string Name,
    TypeExpression Type,
    string? Description = null
);