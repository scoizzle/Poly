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
    /// Export visibility. <see cref="Visibility.Internal"/> unless marked <c>public</c>.
    /// </summary>
    public Visibility Visibility { get; init; } = Visibility.Internal;

    /// <summary>
    /// The navigation properties declared on this entity. A relationship is a
    /// source-entity-owned navigation; there is no domain-global relationship member —
    /// the analysis catalog derives the relationship view from these.
    /// </summary>
    public IReadOnlyList<Relationship> Navigations { get; init; } = [];

    /// <summary>
    /// The compile-time key designation (#405): the names of the entity's own properties, or of its
    /// singular navigations, that together are its domain identity. Empty means the entity is unkeyed
    /// — it has no domain identity and cannot be a key scope or foreign-key target. A designation is
    /// metadata over existing members, not a member: nothing is lowered for it, and
    /// <c>Id</c> is an alias of this tuple (<see cref="Analysis.EntityStructureMetadata.Id"/>).
    /// Authored as the header clause <c>key(a, b)</c> immediately before the entity's <c>{</c> and
    /// printed in that position. Names are scoped to this entity; analysis rejects a malformed
    /// designation. Internal: nothing enforces a key at runtime yet, so embedders must not set one. It is
    /// independent of <c>HasNaturalKey</c>/<c>KeyPropertyName</c>; reconciling them belongs to the consuming slice.
    /// </summary>
    internal IReadOnlyList<string> KeyProperties { get; init; } = [];

    /// <summary>
    /// The explicit initial-stage designation (#675): the name of the stage a new instance starts in.
    /// Authored as the header clause <c>initial(Stage)</c> before <c>key(...)</c> and printed in that
    /// position. With no designation the first declared stage is initial, which is the implicit rule.
    /// A designation is metadata over an existing stage, not a member: nothing is lowered for it, and
    /// <see cref="EntityStages.InitialStage"/> resolves it, falling back to the first declared stage
    /// when no stage carries the name; analysis reports that as an error. Internal until a consuming
    /// slice widens it (S-14, D-04, Q-12).
    /// </summary>
    internal string? InitialStageName { get; init; }

    /// <summary>
    /// The order in which this entity's members were authored (S-15, Q12): properties,
    /// navigations, stages, entity-level actions and entity-level policies, interleaved
    /// exactly as written. Each entry is a <c>(Kind, Name)</c> pair, because names are not
    /// unique across kinds — a stage and a property may share one.
    /// <para>
    /// Authoritative only when it covers every member. A programmatically built or mutated
    /// entity (builder evolution such as <c>EvolveTool</c>) can leave it empty or partial, so
    /// a consumer must not treat an absent entry as "not a member".
    /// </para>
    /// <para>
    /// Written only by <c>SetEntityMemberOrderChange</c>; no member change appends to it and
    /// nothing sorts it. Deliberately not in <see cref="Children"/>: it names members that
    /// are already children, and walking it again would double-walk every member in every pass.
    /// </para>
    /// </summary>
    internal IReadOnlyList<EntityMemberRef> MemberOrder { get; init; } = [];

    public sealed override IEnumerable<Node?> Children =>
        [.. Properties, .. Constraints, .. Facets, .. Actions, .. Policies, .. Stages, .. Subscriptions, .. Navigations];
}