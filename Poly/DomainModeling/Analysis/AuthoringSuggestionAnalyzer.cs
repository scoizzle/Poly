using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Lint-only: actionable authoring suggestions (hints) for domain models.
/// Advisory — does not block evolution; writes no metadata others read.
/// </summary>
internal sealed class AuthoringSuggestionAnalyzer : INodeAnalyzer {
    public const string Id = "DomainAuthoringSuggestionAnalyzer";
    public string PassName => Id;
    // Reads DomainTypeLookupMetadata for entity enumeration.
    public string[] Dependencies => [DomainCatalogPass.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node))
            return;

        if (node is Domain domain) {
            ValidateDomainSuggestions(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateDomainSuggestions(AnalysisContext context, Domain domain) {
        var lookup = context.GetTypeLookup();
        if (lookup is null) return;

        foreach (var entity in lookup.Entities) {
            SuggestMissingStages(context, entity);
            SuggestMissingActions(context, entity);
            SuggestMissingPolicies(context, entity);
            SuggestUnconditionalActions(context, entity);
        }
    }

    /// <summary>
    /// Suggests adding require gates when an action has no guards and no parameters.
    /// (DMBEH001 — hint only, never blocks evolution.)
    /// </summary>
    private static void SuggestUnconditionalActions(AnalysisContext context, Entity entity) {
        // Only warn when the entity has policies elsewhere — otherwise unconditional
        // actions are likely intentional (no guard pattern established at all).
        var hasPoliciesElsewhere = entity.Policies.Count > 0
            || entity.Actions.Any(a => a.Policies.Count > 0)
            || entity.Stages.Any(s => s.Policies.Count > 0 || s.Actions.Any(a => a.Policies.Count > 0));

        foreach (var action in entity.Actions) {
            if (action.Policies.Count == 0 && action.Parameters.Count == 0 && hasPoliciesElsewhere) {
                context.ReportHint(action,
                    $"Action '{entity.Name}.{action.Name}' has no require gates and no parameters — " +
                    "it is unconditionally invocable. Consider adding a 'require PolicyName' guard " +
                    "if business rules should gate this action.",
                    DomainModelDiagnosticCodes.UnconditionalAction);
            }
        }
        foreach (var stage in entity.Stages) {
            foreach (var action in stage.Actions) {
                if (action.Policies.Count == 0 && action.Parameters.Count == 0 && hasPoliciesElsewhere) {
                    context.ReportHint(action,
                        $"Action '{entity.Name}.{action.Name}' (stage '{stage.Name}') has no require gates " +
                        "and no parameters — it is unconditionally invocable. Consider adding " +
                        "a 'require PolicyName' guard if business rules should gate this action.",
                        DomainModelDiagnosticCodes.UnconditionalAction);
                }
            }
        }
    }

    /// <summary>
    /// Suggests adding lifecycle stages when an entity has properties but no stages.
    /// </summary>
    private static void SuggestMissingStages(AnalysisContext context, Entity entity) {
        if (entity.Stages.Count > 0)
            return;
        if (entity.Properties.Count == 0)
            return;

        context.ReportHint(
            entity,
            $"Entity '{entity.Name}' has {entity.Properties.Count} properties but no stages defined. " +
            "Consider adding lifecycle stages (e.g. 'Active', 'Inactive') to model state transitions. " +
            "Use 'add_stage' to add stages.",
            DomainModelDiagnosticCodes.AuthoringSuggestion);
    }

    /// <summary>
    /// Suggests adding actions when an entity has stages but none of them have actions,
    /// and there are no entity-level actions either.
    /// </summary>
    private static void SuggestMissingActions(AnalysisContext context, Entity entity) {
        if (entity.Actions.Count > 0)
            return;
        if (entity.Stages.Count == 0)
            return;

        bool anyStageAction = entity.Stages.Any(s => s.Actions.Count > 0);
        if (anyStageAction)
            return;

        context.ReportHint(
            entity,
            $"Entity '{entity.Name}' has {entity.Stages.Count} stages but no actions defined on any stage. " +
            "Actions define what operations can be performed in each state. " +
            "Use 'add_action_to_stage' to add actions to stages.",
            DomainModelDiagnosticCodes.AuthoringSuggestion);
    }

    /// <summary>
    /// Suggests adding policies when an entity has boolean properties or properties
    /// with range constraints that could benefit from guard expressions.
    /// </summary>
    private static void SuggestMissingPolicies(AnalysisContext context, Entity entity) {
        if (entity.Policies.Count > 0)
            return;

        bool hasPolicyRelevantProps = entity.Properties.Any(p =>
            string.Equals(p.Type.TypeName, "Boolean", StringComparison.Ordinal) ||
            p.Constraints.Any(c => c is Constraints.RangeConstraint));

        if (!hasPolicyRelevantProps)
            return;

        context.ReportHint(
            entity,
            $"Entity '{entity.Name}' has properties suitable for policy guards " +
            "(boolean or range-constrained properties) but no policies defined. " +
            "Policies enforce business rules. Use `add(kind: policy)` or `apply_dsl` to define guards.",
            DomainModelDiagnosticCodes.AuthoringSuggestion);
    }
}