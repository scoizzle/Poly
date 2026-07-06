using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class CausalityAnalyzer : INodeAnalyzer {
    public const string Id = "DomainCausalityAnalyzer";
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
        if (!context.TryBeginAnalyzerVisit<CausalityAnalyzer>(domain)) {
            return;
        }

        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is null) {
            return;
        }

        var entityActionMap = BuildEntityActionMap(domain);
        var eventSubscriptionMap = BuildEventSubscriptionMap(domain);
        var graph = BuildActionGraph(domain, entityActionMap, eventSubscriptionMap);
        var cycles = FindCycles(graph);

        foreach (var action in cycles) {
            context.ReportWarning(
                action,
                $"Action '{action.Name}' participates in a causality cycle.",
                DomainModelDiagnosticCodes.ActionOrderingCausality);
        }
    }

    private static Dictionary<Entity, Dictionary<string, Action>> BuildEntityActionMap(Domain domain) {
        var map = new Dictionary<Entity, Dictionary<string, Action>>(ReferenceEqualityComparer.Instance);
        foreach (var type in domain.Types) {
            if (type is Entity entity) {
                var actions = entity.Actions.ToDictionary(
                    static a => a.Name, StringComparer.Ordinal);
                map[entity] = actions;
            }
        }
        return map;
    }

    private static Dictionary<string, List<(Entity Entity, string HandlerActionName)>> BuildEventSubscriptionMap(Domain domain) {
        var map = new Dictionary<string, List<(Entity, string)>>(StringComparer.Ordinal);
        foreach (var type in domain.Types) {
            if (type is Entity entity) {
                foreach (var sub in entity.EventSubscriptions) {
                    if (!map.TryGetValue(sub.EventType.TypeName, out var list)) {
                        list = [];
                        map[sub.EventType.TypeName] = list;
                    }
                    list.Add((entity, sub.HandlerActionName));
                }
            }
        }
        return map;
    }

    private static Dictionary<Action, HashSet<Action>> BuildActionGraph(
        Domain domain,
        Dictionary<Entity, Dictionary<string, Action>> entityActions,
        Dictionary<string, List<(Entity Entity, string HandlerActionName)>> eventSubscriptions) {

        var graph = new Dictionary<Action, HashSet<Action>>(ReferenceEqualityComparer.Instance);

        foreach (var type in domain.Types) {
            if (type is not Entity entity) continue;
            if (!entityActions.TryGetValue(entity, out var actionMap)) continue;

            foreach (var action in entity.Actions) {
                if (!graph.TryGetValue(action, out var edges)) {
                    edges = [];
                    graph[action] = edges;
                }

                foreach (var effect in FlattenEffects(action.Effects)) {
                    switch (effect) {
                        case InvokeActionEffect iae when actionMap.TryGetValue(iae.ActionName, out var target):
                            edges.Add(target);
                            break;
                        case PublishEventEffect pee when eventSubscriptions.TryGetValue(pee.EventType.TypeName, out var subscribers):
                            foreach (var (subEntity, handlerName) in subscribers) {
                                if (entityActions.TryGetValue(subEntity, out var subActions)
                                    && subActions.TryGetValue(handlerName, out var handler)) {
                                    edges.Add(handler);
                                }
                            }
                            break;
                    }
                }
            }
        }

        return graph;
    }

    private static HashSet<Action> FindCycles(Dictionary<Action, HashSet<Action>> graph) {
        var cycles = new HashSet<Action>(ReferenceEqualityComparer.Instance);
        var visiting = new HashSet<Action>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<Action>(ReferenceEqualityComparer.Instance);

        foreach (var node in graph.Keys) {
            Visit(node);
        }

        return cycles;

        void Visit(Action action) {
            if (visited.Contains(action)) return;
            if (!visiting.Add(action)) {
                cycles.Add(action);
                return;
            }

            if (graph.TryGetValue(action, out var edges)) {
                foreach (var edge in edges) {
                    if (visiting.Contains(edge)) {
                        cycles.Add(action);
                        cycles.Add(edge);
                        continue;
                    }
                    Visit(edge);
                }
            }

            visiting.Remove(action);
            visited.Add(action);
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