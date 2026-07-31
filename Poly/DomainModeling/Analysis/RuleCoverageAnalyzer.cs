using Poly.Analysis;
using Poly.DomainModeling.Effects;

namespace Poly.DomainModeling.Analysis;

/// <summary>Lint-only: rule coverage hints; writes no metadata others read.</summary>
internal sealed class RuleCoverageAnalyzer : INodeAnalyzer {
    public const string Id = "DomainRuleCoverageAnalyzer";
    public string PassName => Id;
    // Reads RequiredPropertiesMetadata published by RequiredPropertiesPass.
    public string[] Dependencies => [RequiredPropertiesPass.Id];
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        if (node is Domain domain) {
            ValidateDomain(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateDomain(AnalysisContext context, Domain domain) {
        DomainAnalysis.ForEachEntity(domain, entity => {
            var requiredMeta = context.GetMetadata<RequiredPropertiesMetadata>(entity);
            if (requiredMeta is null || requiredMeta.RequiredProperties.Count == 0) return;

            foreach (var action in entity.Actions) {
                ValidateAction(context, action, requiredMeta.RequiredProperties);
            }
        });
    }

    private static void ValidateAction(AnalysisContext context, Action action, IReadOnlyList<Property> requiredProperties) {
        var hasStageTransition = FlattenEffects(action.Effects).Any(static e => e is StageTransitionEffect);
        if (!hasStageTransition) return;

        var coveredNames = FlattenEffects(action.Effects)
            .OfType<AssignEffect>()
            .Select(static ae => ae.Target)
            .OfType<PropertyAccess>()
            .Select(static pa => pa.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = requiredProperties
            .Where(rp => !coveredNames.Contains(rp.Name))
            .ToArray();

        if (missing.Length > 0) {
            context.ReportHint(
                action,
                $"Action '{action.Name}' transitions to a new stage but has coverage gaps: one or more required properties are not assigned in mutation effects.",
                DomainModelDiagnosticCodes.RuleCoverage);
        }
    }

    private static IEnumerable<Effect> FlattenEffects(IEnumerable<Effect> effects) =>
        EffectHelpers.FlattenEffects(effects);
}