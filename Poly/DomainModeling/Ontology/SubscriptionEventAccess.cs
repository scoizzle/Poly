namespace Poly.DomainModeling.Ontology;

/// <summary>
/// Legacy naming constants for the retired subscription <c>event</c> root.
/// Product path uses optional peer binders: <c>when Rel Stage as name { … name Prop … }</c>.
/// </summary>
/// <remarks>
/// Analysis rejects <c>event</c> / <c>event.Prop</c> in subscription effects.
/// Kept so diagnostics and migration greps have a single identifier.
/// </remarks>
public static class SubscriptionEventAccess {
    /// <summary>Retired dictionary-key prefix (<c>event.Name</c>).</summary>
    public const string Prefix = "event.";

    /// <summary>Retired path-prefix root name (<c>event Prop</c>).</summary>
    public const string RelationshipName = "event";
}