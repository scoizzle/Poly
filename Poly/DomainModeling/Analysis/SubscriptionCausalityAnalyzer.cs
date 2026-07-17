using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Coarse heuristic for detecting mutual subscription cycles.
/// Builds edges from each subscriber entity to the target entity of its subscription's
/// relationship. Any directed cycle in this graph is reported as a potential causality risk.
///
/// This is a **simplified model**: it assumes any subscription on Entity A targeting
/// Entity B means A reacts to B's stage changes. A true cycle would require tracking
/// action→stage-transition→subscription edges precisely, which requires analysis metadata
/// about which actions emit which <see cref="StageTransitionEffect"/>.
///
/// TODO (post-B): Rewire to use action-level transition metadata for precise cycle detection.
/// </summary>
internal sealed class SubscriptionCausalityAnalyzer : INodeAnalyzer {
    public const string Id = "DomainSubscriptionCausalityAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) return;

        if (node is Domain domain) {
            ValidateDomain(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateDomain(AnalysisContext context, Domain domain) {
        if (!context.TryBeginAnalyzerVisit<SubscriptionCausalityAnalyzer>(domain)) return;

        var entityMap = domain.Types
            .OfType<Entity>()
            .GroupBy(static e => e.Name, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.Ordinal);

        // Build a graph: for each subscription on each stage, track which entity
        // subscribes to which other entity's stage transitions via which action.
        var edges = new List<(string FromEntity, string ToEntity, string StageName)>();

        foreach (var entity in entityMap.Values) {
            foreach (var stage in entity.Stages) {
                foreach (var sub in stage.Subscriptions) {
                    // Subscription targets: all entities referenced by the relationship name.
                    // For now, scan relationships for a match on this entity.
                    var targetEntities = domain.Relationships
                        .Where(r => string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal)
                                 && string.Equals(r.Name, sub.RelationshipName, StringComparison.Ordinal))
                        .Select(r => r.Target.TypeName)
                        .Distinct(StringComparer.Ordinal);

                    foreach (var targetEntity in targetEntities) {
                        if (entityMap.ContainsKey(targetEntity)) {
                            edges.Add((entity.Name, targetEntity, sub.StageNames.FirstOrDefault() ?? "?"));
                        }
                    }
                }
            }
        }

        // Detect cycles using DFS
        var allEntities = entityMap.Keys.ToList();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var inStack = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in allEntities) {
            if (!visited.Contains(entity)) {
                var cycleParticipants = DetectCycle(entity, edges, visited, inStack);
                if (cycleParticipants.Count > 0) {
                    // Report on the entity that closes the cycle
                    if (entityMap.TryGetValue(cycleParticipants[0], out var cycleEntity)) {
                        context.ReportWarning(
                            cycleEntity,
                            $"Subscription causality cycle detected involving entities: {string.Join(" → ", cycleParticipants)}.",
                            DomainModelDiagnosticCodes.SubscriptionCausalityCycle);
                    }
                    break; // Report one cycle at a time
                }
            }
        }
    }

    private static List<string> DetectCycle(
        string current,
        List<(string From, string To, string Stage)> edges,
        HashSet<string> visited,
        HashSet<string> inStack) {

        visited.Add(current);
        inStack.Add(current);

        var successors = edges
            .Where(e => string.Equals(e.From, current, StringComparison.Ordinal))
            .Select(e => e.To)
            .Distinct(StringComparer.Ordinal);

        foreach (var successor in successors) {
            if (inStack.Contains(successor)) {
                return [current, successor];
            }

            if (!visited.Contains(successor)) {
                var result = DetectCycle(successor, edges, visited, inStack);
                if (result.Count > 0) {
                    return result;
                }
            }
        }

        inStack.Remove(current);
        return [];
    }
}