using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class EventContractAnalyzer : INodeAnalyzer {
    public static string PassId => "DomainEventContractAnalyzer";
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
        if (!context.TryBeginAnalyzerVisit<EventContractAnalyzer>(entity)) {
            return;
        }

        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is null) return;

        foreach (var subscription in entity.EventSubscriptions) {
            ValidateSubscription(context, subscription, entity, lookup);
        }
    }

    private static void ValidateSubscription(
        AnalysisContext context,
        EventSubscription subscription,
        Entity entity,
        DomainTypeLookupMetadata lookup) {

        var handler = entity.Actions.FirstOrDefault(a =>
            string.Equals(a.Name, subscription.HandlerActionName, StringComparison.Ordinal));
        if (handler is null) return;

        var eventParam = handler.Parameters.FirstOrDefault(p =>
            string.Equals(p.Name, subscription.EventParameterName, StringComparison.Ordinal));
        if (eventParam is null) return;

        if (!lookup.Types.TryGetValue(eventParam.Type.TypeName, out var paramType) || paramType is not Event paramEvent) {
            return;
        }

        if (!lookup.Types.TryGetValue(subscription.EventType.TypeName, out var subType) || subType is not Event subEvent) {
            return;
        }

        foreach (var contractProperty in subEvent.Properties) {
            var candidate = paramEvent.Properties
                .FirstOrDefault(p => string.Equals(p.Name, contractProperty.Name, StringComparison.Ordinal));
            if (candidate is null) {
                context.ReportError(
                    handler,
                    $"Event handler '{handler.Name}' parameter '{eventParam.Name}' is missing property '{contractProperty.Name}' required by event '{subEvent.Name}'.",
                    DomainModelDiagnosticCodes.ActionEventContract);
                continue;
            }

            if (!string.Equals(candidate.Type.TypeName, contractProperty.Type.TypeName, StringComparison.Ordinal)) {
                context.ReportError(
                    handler,
                    $"Event handler '{handler.Name}' parameter '{eventParam.Name}' property '{candidate.Name}' has type '{candidate.Type.TypeName}' but event '{subEvent.Name}' expects '{contractProperty.Type.TypeName}'.",
                    DomainModelDiagnosticCodes.ActionEventContract);
            }
        }
    }
}