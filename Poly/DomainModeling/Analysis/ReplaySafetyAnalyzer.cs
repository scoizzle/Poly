using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class ReplaySafetyAnalyzer : INodeAnalyzer {
    public const string Id = "DomainReplaySafetyAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];
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
        if (!context.TryBeginAnalyzerVisit<ReplaySafetyAnalyzer>(domain)) {
            return;
        }

        foreach (var type in domain.Types) {
            if (type is Entity entity) {
                ValidateEntity(context, entity);
            }
        }
    }

    private static void ValidateEntity(AnalysisContext context, Entity entity) {
        var handlerNames = entity.EventSubscriptions
            .Select(static s => s.HandlerActionName)
            .ToHashSet(StringComparer.Ordinal);

        if (handlerNames.Count == 0) {
            return;
        }

        foreach (var action in entity.Actions) {
            if (handlerNames.Contains(action.Name)) {
                ValidateAction(context, action);
            }
        }
    }

    private static void ValidateAction(AnalysisContext context, Action action) {
        var hasNonIdempotent = FlattenEffects(action.Effects).Any(static e =>
            e is CreateEntityInstance or LinkRelationshipEffect or PublishEventEffect);

        if (hasNonIdempotent) {
            context.ReportWarning(
                action,
                $"Event-handler action '{action.Name}' has idempotency risk under replay because it performs create/link/publish effects.",
                DomainModelDiagnosticCodes.ActionIdempotencyReplay);
        }
    }

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