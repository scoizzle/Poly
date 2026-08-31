using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Single composition algorithm for stage-effective policies and actions.
/// <para>
/// <b>Canonical surface:</b> <see cref="StageCapabilityMetadata"/> / <see cref="StageCapabilityView"/>
/// published by <see cref="CapabilityAnalyzer"/>. Downstream reads that surface only.
/// </para>
/// <para>
/// <b>Composition rules:</b>
/// <list type="bullet">
/// <item><b>Effective policies at stage</b> = stage-local policies only.
/// Named entity policies are predicates (<c>require</c> / <c>for where</c>), not always-on
/// stage invariants. Action-scoped policies are not stage-effective.</item>
/// <item><b>Effective actions at stage</b> = stage-local actions only.
/// No stage-parent hierarchy; entity-level actions are resolved at runtime via
/// <c>TryResolveAction</c> (SA fallthrough), not folded into stage-effective lists.</item>
/// </list>
/// </para>
/// </summary>
internal static class DomainEffectiveSurface {
    public static IReadOnlyList<Policy> ComposeStagePolicies(Stage stage) =>
        stage.Policies.Count == 0 ? Array.Empty<Policy>() : stage.Policies;
}
