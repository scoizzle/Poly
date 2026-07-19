namespace Poly.DomainModeling;

/// <summary>
/// Shared conventions for accessing the transitioning ("event") instance
/// from stage-subscription effects. Used by runtime injection and analysis.
/// </summary>
/// <remarks>
/// The VM member-resolution path does not yet fully resolve these keys;
/// see <see cref="DomainEntityInstance.ExecuteSubscriptionEffects"/>.
/// </remarks>
public static class SubscriptionEventAccess {
    /// <summary>Dictionary-key prefix for event instance properties (<c>event.Name</c>).</summary>
    public const string Prefix = "event.";

    /// <summary>
    /// Relationship name used when subject-first path-prefix is written as
    /// <c>event PropName</c> (lowered to <see cref="RelationshipNavigation"/>).
    /// </summary>
    public const string RelationshipName = "event";
}