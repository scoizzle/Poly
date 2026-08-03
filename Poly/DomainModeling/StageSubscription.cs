namespace Poly.DomainModeling;

/// <summary>
/// Determines how a <see cref="StageSubscription"/> reacts when related entities
/// transition into a matching stage.
/// </summary>
public enum StageSubscriptionQuantifier {
    /// <summary>
    /// Default. Fires effects each time a single related entity enters a matching stage.
    /// </summary>
    Each,

    /// <summary>
    /// Fires effects once when at least one related entity is in a matching stage.
    /// </summary>
    Any,

    /// <summary>
    /// Fires effects once when every related entity is in a matching stage.
    /// </summary>
    All,
}

/// <summary>
/// A stage-scoped subscription that fires effects when a related entity (reachable via
/// <see cref="RelationshipName"/>) transitions into any of the listed <see cref="StageNames"/>.
/// </summary>
/// <remarks>
/// Subscriptions live on the <see cref="Stage"/> that declares them. They are active only
/// while the subscriber entity occupies that stage. The <see cref="Quantifier"/> controls
/// how collections are evaluated (per-element, any-match, or all-match).
///
/// Effect-body subjects:
/// <list type="bullet">
///   <item>Bare property names resolve to the <b>subscriber</b> instance.</item>
///   <item>When <see cref="PeerBinding"/> is set (<c>when Rel Stage as name</c>), path-prefix
///     <c>name Prop</c> resolves to the <b>transitioned peer</b> for this firing.</item>
///   <item>When <see cref="PeerBinding"/> is null, the subscription is notification-only
///     (stage signal without peer field access).</item>
/// </list>
/// </remarks>
public sealed record StageSubscription(
    string RelationshipName,
    IReadOnlyList<string> StageNames,
    StageSubscriptionQuantifier Quantifier,
    IReadOnlyList<Effect> Effects,
    string? PeerBinding = null
) : DomainObject {
    public StageSubscription(string relationshipName, string stageName, StageSubscriptionQuantifier quantifier, IReadOnlyList<Effect> effects, string? peerBinding = null)
        : this(relationshipName, [stageName], quantifier, effects, peerBinding) { }

    public StageSubscription(string relationshipName, string stageName, IReadOnlyList<Effect> effects, string? peerBinding = null)
        : this(relationshipName, [stageName], StageSubscriptionQuantifier.Each, effects, peerBinding) { }

    public sealed override IEnumerable<Node?> Children => [.. Effects];
}