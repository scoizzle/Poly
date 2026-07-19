using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Detects mutual subscription cycles using analysis metadata.
/// Builds edges from each subscriber entity to the target entity of its subscription's
/// relationship, then filters to only include edges where the target entity has at least
/// one action that emits a <see cref="StageTransitionEffect"/> to a stage the subscription
/// watches. This eliminates false positives for subscriptions targeting entities whose
/// stages are never entered via any action.
///
/// Uses <see cref="DomainTypeLookupMetadata"/> for entity resolution and
/// <see cref="ActionCapabilityMetadata"/> for action→transition mapping.
/// </summary>
internal sealed class SubscriptionCausalityAnalyzer : INodeAnalyzer {
    public const string Id = "DomainSubscriptionCausalityAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [CapabilityAnalyzer.Id];

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

        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is null) return;

        // Build a precise edge graph using capability metadata.
        // An edge E₁ → E₂ exists only when E₂ has at least one action that
        // transitions to a stage that E₁'s subscription watches.
        var edges = new List<(string FromEntity, string ToEntity, string StageName)>();

        foreach (var entity in lookup.Entities) {
            foreach (var stage in entity.Stages) {
                foreach (var sub in stage.Subscriptions) {
                    // Resolve target entities from the subscription's relationship
                    var targetEntities = domain.Relationships
                        .Where(r => string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal)
                                 && string.Equals(r.Name, sub.RelationshipName, StringComparison.Ordinal))
                        .Select(r => r.Target.TypeName)
                        .Distinct(StringComparer.Ordinal);

                    foreach (var targetName in targetEntities) {
                        if (!lookup.Types.TryGetValue(targetName, out var targetType) || targetType is not Entity targetEntity)
                            continue;

                        // Precise check: does targetEntity have any action that
                        // transitions to a stage this subscription watches?
                        if (!TargetHasTransitionToWatchedStages(context, targetEntity, sub.StageNames))
                            continue;

                        edges.Add((entity.Name, targetEntity.Name, sub.StageNames.FirstOrDefault() ?? "?"));
                    }
                }
            }
        }

        // Detect cycles using DFS
        var allEntities = lookup.Entities.Select(e => e.Name).ToList();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var inStack = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entityName in allEntities) {
            if (!visited.Contains(entityName)) {
                var cycleParticipants = DetectCycle(entityName, edges, visited, inStack);
                if (cycleParticipants.Count > 0) {
                    if (lookup.Types.TryGetValue(cycleParticipants[0], out var cycleType) && cycleType is Entity cycleEntity) {
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

    /// <summary>
    /// Returns true if <paramref name="targetEntity"/> has at least one action (entity-level
    /// or stage-scoped) whose <see cref="ActionCapabilityMetadata"/> reports a transition to
    /// any of the given <paramref name="watchedStages"/>.
    /// </summary>
    private static bool TargetHasTransitionToWatchedStages(
        AnalysisContext context,
        Entity targetEntity,
        IReadOnlyList<string> watchedStages) {
        var watched = new HashSet<string>(watchedStages, StringComparer.Ordinal);

        foreach (var action in targetEntity.Actions) {
            var cap = context.GetMetadata<ActionCapabilityMetadata>(action);
            if (cap is null) continue;
            if (cap.View.TransitionTargets.Any(t => watched.Contains(t.Name)))
                return true;
        }

        foreach (var stage in targetEntity.Stages) {
            foreach (var action in stage.Actions) {
                var cap = context.GetMetadata<ActionCapabilityMetadata>(action);
                if (cap is null) continue;
                if (cap.View.TransitionTargets.Any(t => watched.Contains(t.Name)))
                    return true;
            }
        }

        return false;
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