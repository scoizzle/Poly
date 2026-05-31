namespace Poly.DomainModeling;

/// <summary>
/// An <see cref="Entity"/> owns properties, can participate in stages, expose actions, publish and subscribe to events,
/// and carry policies. It is the central concept for modeling stateful business objects with behavior.
/// </summary>
/// <remarks>
/// Entities are the main units that have stages (<see cref="Stage"/>), actions, and lifecycle effects.
/// They differ from <see cref="ValueType"/> in that they have identity and behavior over time.
/// </remarks>
public sealed record Entity(
    string Name,
    IReadOnlyList<Property> Properties,
    IReadOnlyList<DomainTypeReference> Events,
    IReadOnlyList<Action> Actions,
    IReadOnlyList<Policy> Policies,
    IReadOnlyList<Stage> Stages
) : DomainType(Name, Properties, []) {
    public sealed override IEnumerable<Node?> Children => [.. Properties, .. Events, .. Constraints, .. Actions, .. Policies, .. Stages];
}