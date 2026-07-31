namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Single composition algorithm for stage-effective policies and actions (DAS W2).
/// <para>
/// <b>Canonical surface:</b> <see cref="StageCapabilityMetadata"/> / <see cref="StageCapabilityView"/>
/// published by <see cref="CapabilityAnalyzer"/>. Helpers and MCP describe routes read that
/// surface (or re-apply these rules over the catalog when the capability bag is absent).
/// </para>
/// <para>
/// <b>Composition rules:</b>
/// <list type="bullet">
/// <item><b>Effective policies at stage</b> = entity-level policies + stage-local policies.
/// Action-scoped policies are not stage-effective (they guard actions, not the stage).</item>
/// <item><b>Effective actions at stage</b> = stage-local actions only.
/// No stage-parent hierarchy; entity-level actions are resolved at runtime via
/// <c>TryResolveAction</c> (SA fallthrough), not folded into stage-effective lists.</item>
/// </list>
/// </para>
/// </summary>
internal static class DomainEffectiveSurface {
    public static IReadOnlyList<Policy> ComposeStagePolicies(
        IReadOnlyList<Policy> entityPolicies,
        Stage stage) {
        if (entityPolicies.Count == 0 && stage.Policies.Count == 0)
            return Array.Empty<Policy>();
        if (entityPolicies.Count == 0)
            return stage.Policies;
        if (stage.Policies.Count == 0)
            return entityPolicies;
        List<Policy> combined = [.. entityPolicies, .. stage.Policies];
        return combined;
    }

    public static IReadOnlyList<Policy> ComposeStagePolicies(
        IReadOnlyDictionary<string, Policy>? entityPoliciesByName,
        IReadOnlyDictionary<string, Policy>? stagePoliciesByName) {
        var entity = entityPoliciesByName?.Values.ToList() ?? [];
        var stage = stagePoliciesByName?.Values.ToList() ?? [];
        if (entity.Count == 0 && stage.Count == 0)
            return Array.Empty<Policy>();
        if (entity.Count == 0)
            return stage;
        if (stage.Count == 0)
            return entity;
        entity.AddRange(stage);
        return entity;
    }

    public static IReadOnlyList<Action> ComposeStageActions(Stage stage) =>
        stage.Actions.Count == 0 ? Array.Empty<Action>() : stage.Actions;
}