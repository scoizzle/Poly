using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analyzer that produces actionable authoring suggestions (hints) for domain models.
/// These are advisory — they do not block evolution, but help agents identify
/// common gaps in lifecycle definitions.
/// </summary>
internal sealed class AuthoringSuggestionAnalyzer : INodeAnalyzer {
    public const string Id = "DomainAuthoringSuggestionAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];

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
        if (!context.TryBeginAnalyzerVisit<AuthoringSuggestionAnalyzer>(domain))
            return;

        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is null) return;

        foreach (var entity in lookup.Entities) {
            SuggestMissingStages(context, entity);
            SuggestMissingActions(context, entity);
            SuggestMissingPolicies(context, entity);
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
            "Policies enforce business rules. Use 'add_policy' to define guards.",
            DomainModelDiagnosticCodes.AuthoringSuggestion);
    }
}