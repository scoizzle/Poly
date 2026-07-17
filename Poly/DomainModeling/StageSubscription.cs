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
/// Implicit bindings in effect bodies:
/// <list type="bullet">
///   <item><c>this</c> resolves to the subscriber entity instance.</item>
///   <item><c>event</c> resolves to the transitioning entity instance.</item>
/// </list>
/// </remarks>
public sealed record StageSubscription(
    string RelationshipName,
    IReadOnlyList<string> StageNames,
    StageSubscriptionQuantifier Quantifier,
    IReadOnlyList<Effect> Effects
) : DomainObject {
    public StageSubscription(string relationshipName, string stageName, StageSubscriptionQuantifier quantifier, IReadOnlyList<Effect> effects)
        : this(relationshipName, [stageName], quantifier, effects) { }

    public StageSubscription(string relationshipName, string stageName, IReadOnlyList<Effect> effects)
        : this(relationshipName, [stageName], StageSubscriptionQuantifier.Each, effects) { }

    public sealed override IEnumerable<Node?> Children => [.. Effects];
}