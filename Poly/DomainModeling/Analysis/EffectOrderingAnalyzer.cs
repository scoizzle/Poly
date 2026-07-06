using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class EffectOrderingAnalyzer : INodeAnalyzer {
    public static string PassId => "DomainEffectOrderingAnalyzer";
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
        if (!context.TryBeginAnalyzerVisit<EffectOrderingAnalyzer>(domain)) {
            return;
        }

        foreach (var type in domain.Types) {
            if (type is Entity entity) {
                foreach (var action in entity.Actions) {
                    ValidateActionEffects(context, action);
                }
                foreach (var stage in entity.Stages) {
                    ValidateActionEffects(context, stage.OnEntryEffects);
                    ValidateActionEffects(context, stage.OnExitEffects);
                }
            }
        }
    }

    private static void ValidateActionEffects(AnalysisContext context, Action action) {
        ValidateActionEffects(context, action.Effects);
    }

    private static void ValidateActionEffects(AnalysisContext context, IReadOnlyList<Effect> effects) {
        var flattened = FlattenEffects(effects).ToArray();
        var deleteIndex = Array.FindIndex(flattened, static e => e is DeleteEntityInstance);
        if (deleteIndex < 0) {
            return;
        }

        if (flattened.Skip(deleteIndex + 1).Any(IsMutatingEffect)) {
            context.ReportWarning(
                effects.Count > 0 ? effects[0] : flattened[0],
                "Mutating effect executes after DeleteEntityInstance, which is a no-op on a deleted instance.",
                DomainModelDiagnosticCodes.EffectPrePostCondition);
        }
    }

    private static bool IsMutatingEffect(Effect effect) =>
        effect is AssignEffect
            or CreateEntityInstance
            or StageTransitionEffect
            or LinkRelationshipEffect
            or UnlinkRelationshipEffect
            or TransitionRelationshipEffect;

    private static IEnumerable<Effect> FlattenEffects(IEnumerable<Effect> effects) {
        foreach (var effect in effects) {
            yield return effect;
            switch (effect) {
                case ConditionalEffect ce:
                    foreach (var nested in FlattenEffects(ce.ThenEffects)) {
                        yield return nested;
                    }
                    if (ce.ElseEffects is not null) {
                        foreach (var nested in FlattenEffects(ce.ElseEffects)) {
                            yield return nested;
                        }
                    }
                    break;
                case CompositeEffect ce:
                    foreach (var nested in FlattenEffects(ce.Effects)) {
                        yield return nested;
                    }
                    break;
            }
        }
    }
}