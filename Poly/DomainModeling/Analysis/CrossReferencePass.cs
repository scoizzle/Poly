using Poly.Analysis;
using Poly.DomainModeling.Lowering;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analysis pass that produces <see cref="EntityDependencyGraphMetadata"/> —
/// a directed graph from entity navigations and subscriptions, with cycle detection.
///
/// Depends on <see cref="EffectTopologyPass"/> (subscription/invoke edges).
/// Ownership aggregate metadata is not required for the graph.
/// </summary>
internal sealed class CrossReferencePass : INodeAnalyzer {
    public const string Id = "CrossReferencePass";
    public string PassName => Id;
    public string[] Dependencies => [EffectTopologyPass.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (context.HasStructuralFailure) return;

        var topology = context.GetMetadata<EffectTopologyMetadata>(domain)?.Topology;
        var relationships = domain.Relationships.ToList();
        var entities = domain.Types.OfType<Entity>().ToList();
        var entityNames = entities.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        var relLookup = relationships.ToDictionary(r => r.Name, StringComparer.Ordinal);

        var edges = new List<EntityDependencyEdge>();
        var relationshipPairs = new HashSet<(string From, string To)>();

        // Full edge list (including pure inverse navigations) for metadata consumers
        foreach (var rel in relationships) {
            if (!entityNames.Contains(rel.Source.TypeName) || !entityNames.Contains(rel.Target.TypeName))
                continue;

            edges.Add(new EntityDependencyEdge(rel.Source.TypeName, rel.Target.TypeName, "Relationship"));
            relationshipPairs.Add((rel.Source.TypeName, rel.Target.TypeName));
        }

        var subscriptionInvokeEdges = new List<(string From, string To)>();
        if (topology != null) {
            foreach (var sub in topology.Subscriptions) {
                if (relLookup.TryGetValue(sub.RelationshipName, out var rel)) {
                    edges.Add(new EntityDependencyEdge(sub.SubscriberEntity, rel.Target.TypeName, "Subscription"));
                    subscriptionInvokeEdges.Add((sub.SubscriberEntity, rel.Target.TypeName));
                }
            }
            foreach (var invoke in topology.CrossEntityInvokes) {
                if (invoke.TargetRelationship != null && relLookup.TryGetValue(invoke.TargetRelationship, out var rel2)) {
                    edges.Add(new EntityDependencyEdge(invoke.SourceEntity, rel2.Target.TypeName, "Invoke"));
                    subscriptionInvokeEdges.Add((invoke.SourceEntity, rel2.Target.TypeName));
                }
            }
        }

        // Cycle detection adjacency: drop pure inverse relationship pairs
        // (Patron.loans ↔ Loan.borrower) which are intentional navigations, not smell cycles.
        // Subscription/invoke edges always participate; 3+ entity relationship cycles still fire.
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (from, to) in relationshipPairs) {
            if (relationshipPairs.Contains((to, from)))
                continue;
            AddEdge(adjacency, from, to);
        }
        foreach (var (from, to) in subscriptionInvokeEdges)
            AddEdge(adjacency, from, to);

        var cycleEntityNames = DetectCycles(adjacency, entityNames);

        context.SetMetadata(domain, new EntityDependencyGraphMetadata(edges, cycleEntityNames));

        if (cycleEntityNames.Count > 0) {
            context.ReportDiagnostic(domain,
                DiagnosticSeverity.Warning,
                $"Cross-entity dependency cycle detected involving: {string.Join(", ", cycleEntityNames)}",
                code: DomainModelDiagnosticCodes.DependencyCycle);
        }
    }

    private static void AddEdge(Dictionary<string, List<string>> adj, string from, string to) {
        if (!adj.TryGetValue(from, out var list))
            adj[from] = list = new();
        if (!list.Contains(to, StringComparer.Ordinal))
            list.Add(to);
    }

    /// <summary>Simple DFS cycle detection. Returns names of entities involved in any cycle.</summary>
    private static IReadOnlyList<string> DetectCycles(
        Dictionary<string, List<string>> adjacency,
        HashSet<string> allNodes) {
        var visited = new Dictionary<string, int>(StringComparer.Ordinal); // 0=unvisited, 1=visiting, 2=done
        var inCycle = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in allNodes)
            visited[node] = 0;

        void Dfs(string current, HashSet<string> path) {
            visited[current] = 1;
            path.Add(current);

            if (adjacency.TryGetValue(current, out var neighbors)) {
                foreach (var next in neighbors) {
                    if (!visited.ContainsKey(next)) {
                        // Not in the graph at all — skip
                        continue;
                    }
                    if (visited[next] == 1) {
                        // Back edge — cycle detected
                        // Mark all nodes from `next` to end of `path`
                        var inPath = false;
                        foreach (var n in path) {
                            if (n == next) inPath = true;
                            if (inPath) inCycle.Add(n);
                        }
                    }
                    else if (visited[next] == 0) {
                        Dfs(next, new HashSet<string>(path));
                    }
                }
            }

            visited[current] = 2;
        }

        foreach (var node in allNodes) {
            if (visited[node] == 0)
                Dfs(node, new HashSet<string>());
        }

        return inCycle.ToList();
    }
}