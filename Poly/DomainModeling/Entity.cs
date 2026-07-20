namespace Poly.DomainModeling;

/// <summary>
/// An <see cref="Entity"/> owns properties, participates in lifecycle stages, exposes actions,
/// and carries policies. It is the central concept for modeling stateful business objects with behavior.
/// </summary>
/// <remarks>
/// Entities are the main units that have stages (<see cref="Stage"/>), actions, and lifecycle effects.
/// They differ from <see cref="ValueType"/> in that they have identity and behavior over time.
///
/// Stage transitions are the observable lifecycle events. Subscriptions are declared via
/// <see cref="StageSubscription"/> on individual stages — no separate event/publish surface.
/// </remarks>
public sealed record Entity(
    string Name,
    IReadOnlyList<Property> Properties,
    IReadOnlyList<Action> Actions,
    IReadOnlyList<Policy> Policies,
    IReadOnlyList<Stage> Stages
) : DomainType(Name, Properties, []) {
    public sealed override IEnumerable<Node?> Children =>
        [.. Properties, .. Constraints, .. Actions, .. Policies, .. Stages];
}