using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class RuleCoverageAnalyzer : INodeAnalyzer {
    public static string PassId => "DomainRuleCoverageAnalyzer";
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
        if (!context.TryBeginAnalyzerVisit<RuleCoverageAnalyzer>(domain)) {
            return;
        }

        foreach (var type in domain.Types) {
            if (type is Entity entity) {
                var requiredMeta = context.GetMetadata<RequiredPropertiesMetadata>(entity);
                if (requiredMeta is null || requiredMeta.RequiredProperties.Count == 0) continue;

                foreach (var action in entity.Actions) {
                    ValidateAction(context, action, requiredMeta.RequiredProperties);
                }
            }
        }
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

    private static IEnumerable<Effect> FlattenEffects(IEnumerable<Effect> effects) {
        foreach (var effect in effects) {
            yield return effect;
            switch (effect) {
                case ConditionalEffect ce:
                    foreach (var nested in FlattenEffects(ce.ThenEffects)) yield return nested;
                    if (ce.ElseEffects is not null)
                        foreach (var nested in FlattenEffects(ce.ElseEffects)) yield return nested;
                    break;
                case CompositeEffect ce:
                    foreach (var nested in FlattenEffects(ce.Effects)) yield return nested;
                    break;
            }
        }
    }
}