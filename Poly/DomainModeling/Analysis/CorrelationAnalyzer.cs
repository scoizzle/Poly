using Poly.DomainModeling.Constraints;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class CorrelationAnalyzer : INodeAnalyzer {
    public const string Id = "DomainCorrelationAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        if (node is Entity entity) {
            ValidateEntity(context, entity);
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateEntity(AnalysisContext context, Entity entity) {
        if (!context.TryBeginAnalyzerVisit<CorrelationAnalyzer>(entity)) {
            return;
        }

        foreach (var subscription in entity.EventSubscriptions) {
            if (subscription.RoutingMode != EventSubscriptionRoutingMode.Correlated) {
                continue;
            }

            ValidateCorrelation(context, subscription, entity);
        }
    }

    private static void ValidateCorrelation(
        AnalysisContext context, EventSubscription subscription, Entity consumerEntity) {
        var duplicateEventKey = subscription.Correlations
            .GroupBy(static b => b.EventPropertyName, StringComparer.Ordinal)
            .Any(static g => g.Count() > 1);
        if (duplicateEventKey) {
            context.ReportWarning(
                subscription,
                $"Correlated subscription maps the same event property more than once.",
                DomainModelDiagnosticCodes.EventCorrelationSoundness);
        }

        var duplicateConsumerKey = subscription.Correlations
            .GroupBy(static b => b.ConsumerPropertyName, StringComparer.Ordinal)
            .Any(static g => g.Count() > 1);
        if (duplicateConsumerKey) {
            context.ReportWarning(
                subscription,
                $"Correlated subscription maps multiple event properties to the same consumer property.",
                DomainModelDiagnosticCodes.EventCorrelationSoundness);
        }

        var correlatedConsumerNames = subscription.Correlations
            .Select(static b => b.ConsumerPropertyName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var property in consumerEntity.Properties) {
            if (property.Constraints.Any(static c => c is RequiredConstraint)) {
                if (!correlatedConsumerNames.Contains(property.Name)) {
                    context.ReportHint(
                        subscription,
                        $"Correlated subscription does not include required consumer property '{property.Name}'.",
                        DomainModelDiagnosticCodes.EventCorrelationSoundness);
                }
            }
        }
    }
}