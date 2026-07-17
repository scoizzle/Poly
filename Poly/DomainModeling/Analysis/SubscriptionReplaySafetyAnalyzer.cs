using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Detects idempotency concerns in stage-subscription triggered effects.
/// If a subscription effect creates entities, publishes events (stage transitions),
/// or establishes links, replaying the subscription could produce duplicate side effects.
/// 
/// Adapted from the previous event-based <c>ReplaySafetyAnalyzer</c>.
/// </summary>
internal sealed class SubscriptionReplaySafetyAnalyzer : INodeAnalyzer {
    public const string Id = "DomainSubscriptionReplaySafetyAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) return;

        if (node is Stage stage) {
            ValidateStage(context, stage);
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateStage(AnalysisContext context, Stage stage) {
        if (!context.TryBeginAnalyzerVisit<SubscriptionReplaySafetyAnalyzer>(stage)) return;

        foreach (var subscription in stage.Subscriptions) {
            ValidateSubscription(context, subscription, stage);
        }
    }

    private static void ValidateSubscription(
        AnalysisContext context,
        StageSubscription subscription,
        Stage stage) {

        var hasNonIdempotent = FlattenEffects(subscription.Effects).Any(static e =>
            e is CreateEntityInstance or StageTransitionEffect
            or LinkRelationshipEffect or UnlinkRelationshipEffect);

        if (hasNonIdempotent) {
            context.ReportHint(
                subscription,
                $"Stage subscription (when {subscription.RelationshipName} -> {string.Join("/", subscription.StageNames)}) " +
                $"has idempotency risk under replay because it performs create, transition, or link effects.",
                DomainModelDiagnosticCodes.SubscriptionIdempotencyReplay);
        }
    }

    private static IEnumerable<Effect> FlattenEffects(IReadOnlyList<Effect> effects) {
        foreach (var effect in effects) {
            yield return effect;
            if (effect is CompositeEffect composite) {
                foreach (var child in FlattenEffects(composite.Effects)) {
                    yield return child;
                }
            }
            if (effect is ConditionalEffect conditional) {
                foreach (var child in FlattenEffects(conditional.ThenEffects)) {
                    yield return child;
                }
                foreach (var child in FlattenEffects(conditional.ElseEffects ?? [])) {
                    yield return child;
                }
            }
        }
    }
}