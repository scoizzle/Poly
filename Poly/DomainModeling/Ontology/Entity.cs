namespace Poly.DomainModeling.Ontology;

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
) : DomainType(Name, Properties, Constraints: []) {
    /// <summary>
    /// Entity-level subscriptions that fire when a related entity transitions into
    /// a matching stage. Unlike stage-scoped subscriptions, these are active regardless
    /// of which stage this entity occupies.
    /// </summary>
    public IReadOnlyList<StageSubscription> Subscriptions { get; init; } = [];

    /// <summary>
    /// The navigation properties declared on this entity. A relationship is a
    /// source-entity-owned navigation; there is no domain-global relationship member —
    /// the analysis catalog derives the relationship view from these.
    /// </summary>
    public IReadOnlyList<Relationship> Navigations { get; init; } = [];

    public sealed override IEnumerable<Node?> Children =>
        [.. Properties, .. Constraints, .. Facets, .. Actions, .. Policies, .. Stages, .. Subscriptions, .. Navigations];
}