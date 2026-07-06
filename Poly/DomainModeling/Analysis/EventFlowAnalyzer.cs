using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class EventFlowAnalyzer : INodeAnalyzer {
    public static string PassId => "DomainEventFlowAnalyzer";
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
        if (!context.TryBeginAnalyzerVisit<EventFlowAnalyzer>(domain)) {
            return;
        }

        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is null) {
            return;
        }

        var allEventNames = lookup.Types
            .Where(static kv => kv.Value is Event)
            .Select(static kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);

        var publishedEventNames = CollectPublishedEvents(domain, lookup).ToHashSet(StringComparer.Ordinal);
        var subscribedEventNames = CollectSubscribedEvents(domain).ToHashSet(StringComparer.Ordinal);

        foreach (var eventName in allEventNames) {
            if (!publishedEventNames.Contains(eventName)) {
                var eventType = lookup.Types[eventName];
                context.ReportHint(
                    eventType,
                    $"Event '{eventName}' is defined but not published by any action.",
                    DomainModelDiagnosticCodes.EventFlowLiveness);
            }
        }

        foreach (var eventName in publishedEventNames) {
            if (!subscribedEventNames.Contains(eventName)) {
                if (!lookup.Types.TryGetValue(eventName, out var eventType)) continue;
                context.ReportHint(
                    eventType,
                    $"Published event '{eventName}' has no subscriptions.",
                    DomainModelDiagnosticCodes.EventFlowLiveness);
            }
        }

        ValidateUnusedEventProperties(context, domain, lookup);
    }

    private static void ValidateUnusedEventProperties(
        AnalysisContext context,
        Domain domain,
        DomainTypeLookupMetadata lookup) {
        var boundEventProperties = CollectBoundEventProperties(domain);

        foreach (var kv in lookup.Types) {
            if (kv.Value is not Event eventType) continue;

            foreach (var prop in eventType.Properties) {
                if (!boundEventProperties.Contains((kv.Key, prop.Name))) {
                    context.ReportHint(
                        prop,
                        $"Event property '{prop.Name}' on '{kv.Key}' is never bound by any PublishEventEffect.",
                        DomainModelDiagnosticCodes.EventFlowLiveness);
                }
            }
        }
    }

    private static HashSet<(string EventName, string PropertyName)> CollectBoundEventProperties(Domain domain) {
        var bound = new HashSet<(string, string)>();

        foreach (var type in domain.Types) {
            if (type is Entity entity) {
                foreach (var action in entity.Actions) {
                    foreach (var effect in FlattenEffects(action.Effects)) {
                        if (effect is PublishEventEffect pee) {
                            foreach (var binding in pee.PropertyBindings) {
                                bound.Add((pee.EventType.TypeName, binding.PropertyName));
                            }
                        }
                    }
                }
            }
        }

        return bound;
    }

    private static IEnumerable<string> CollectPublishedEvents(Domain domain, DomainTypeLookupMetadata lookup) {
        foreach (var type in domain.Types) {
            if (type is Entity entity) {
                foreach (var action in entity.Actions) {
                    foreach (var effect in FlattenEffects(action.Effects)) {
                        if (effect is PublishEventEffect pee) {
                            yield return pee.EventType.TypeName;
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<string> CollectSubscribedEvents(Domain domain) {
        foreach (var type in domain.Types) {
            if (type is Entity entity) {
                foreach (var subscription in entity.EventSubscriptions) {
                    yield return subscription.EventType.TypeName;
                }
            }
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