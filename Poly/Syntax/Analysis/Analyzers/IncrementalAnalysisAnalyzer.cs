namespace Poly.Syntax.Analysis;

public sealed class IncrementalAnalysisAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        var analysisTopology = context.GetIncrementalAnalysisTopology(node);
        if (analysisTopology is null) {
            BuildNodeIdToParentIdDictionary(context, node);
            return;
        }

        var invalidatedNodes = context.GetInvalidatedNodes(node);
        if (invalidatedNodes is null) {
            return;
        }

        BuildAnalysisFilterList(context, node, analysisTopology, invalidatedNodes);
    }

    private static void BuildAnalysisFilterList(AnalysisContext context, Node node, IReadOnlyDictionary<NodeId, NodeId> analysisTopology, IEnumerable<Node> invalidatedNodes) {
        var affectedNodeIds = GetAffectedNodes().ToHashSet();

        foreach (var invalidatedNodeId in affectedNodeIds) {
            context.ClearMetadata(invalidatedNodeId);
            context.ClearDiagnostics(invalidatedNodeId);
        }

        context.SetAffectedNodesForIncrementalAnalysis(node, affectedNodeIds);

        IEnumerable<NodeId> GetAffectedNodes() {
            foreach (var node in invalidatedNodes) {
                yield return node.Id;

                var descendants = new Stack<Node>();
                descendants.Push(node);

                while (descendants.Count > 0) {
                    var current = descendants.Pop();

                    foreach (var child in current.Children) {
                        if (child is null) {
                            continue;
                        }

                        yield return child.Id;
                        descendants.Push(child);
                    }
                }

                var currentId = node.Id;
                while (analysisTopology.TryGetValue(currentId, out var nextParentId)) {
                    yield return nextParentId;
                    currentId = nextParentId;
                }
            }
        }
    }

    private static void BuildNodeIdToParentIdDictionary(AnalysisContext context, Node node) {
        var parentByNodeId = new Dictionary<NodeId, NodeId>();

        Traverse(parentByNodeId, node, parentId: null);

        context.SetIncrementalAnalysisTopology(node, parentByNodeId);
        return;

        static void Traverse(Dictionary<NodeId, NodeId> parentByNodeId, Node current, NodeId? parentId) {
            if (parentId.HasValue && !parentByNodeId.TryAdd(current.Id, parentId.Value)) {
                return;
            }

            foreach (var child in current.Children) {
                if (child is null)
                    continue;

                Traverse(parentByNodeId, child, current.Id);
            }
        }
    }
}

public static class IncrementalAnalysisAnalyzerExtensions {
    private sealed record IncrementalAnalysisMetadata(
        IReadOnlyDictionary<NodeId, NodeId> ParentByNodeId
    ) : IAnalysisMetadata;

    internal sealed record IncrementalAnalysisMutationMetadata(
        IEnumerable<Node> InvalidatedNodes
    ) : IAnalysisMetadata;

    internal sealed record IncrementalAnalysisNodeFilterMetadata(
        IReadOnlyCollection<NodeId> AffectedNodeIds
    ) : IAnalysisMetadata;

    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseIncrementalAnalysis() {
            builder.AddAnalyzer(new IncrementalAnalysisAnalyzer());
            return builder;
        }
    }

    extension(INodeMetadataProvider context) {
        private IncrementalAnalysisMetadata? GetIncrementalAnalysisMetadata(Node root) => context.GetMetadata<IncrementalAnalysisMetadata>(root);
        public bool IsIncrementalAnalysisAvailable(Node root) {
            ArgumentNullException.ThrowIfNull(root);
            return context.GetIncrementalAnalysisMetadata(root) is not null;
        }

        public IReadOnlyDictionary<NodeId, NodeId>? GetIncrementalAnalysisTopology(Node root) {
            ArgumentNullException.ThrowIfNull(root);
            var parentsByNodeIds = context.GetIncrementalAnalysisMetadata(root)?.ParentByNodeId;
            return parentsByNodeIds;
        }

        public IEnumerable<Node>? GetInvalidatedNodes(Node root) {
            ArgumentNullException.ThrowIfNull(root);
            var invalidatedNodes = context.GetMetadata<IncrementalAnalysisMutationMetadata>(root)?.InvalidatedNodes;
            return invalidatedNodes;
        }

        public bool ShouldAnalyze(Node node) {
            ArgumentNullException.ThrowIfNull(node);
            return context.GetMetadata<IncrementalAnalysisNodeFilterMetadata>(node) is not IncrementalAnalysisNodeFilterMetadata filterMetadata
                || filterMetadata.AffectedNodeIds.Contains(node.Id);
        }
    }

    extension(AnalysisContext context) {
        public void SetIncrementalAnalysisTopology(Node root, IReadOnlyDictionary<NodeId, NodeId> parentsByNodeIds) {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(parentsByNodeIds);
            context.SetMetadata(root, new IncrementalAnalysisMetadata(parentsByNodeIds));
        }

        public void SetInvalidatedNodesForIncrementalAnalysis(Node root, IEnumerable<Node> invalidatedNodes) {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(invalidatedNodes);
            if (!context.IsIncrementalAnalysisAvailable(root)) return;
            context.SetMetadata(root, new IncrementalAnalysisMutationMetadata(invalidatedNodes));
        }

        public void SetAffectedNodesForIncrementalAnalysis(Node node, IReadOnlyCollection<NodeId> affectedNodeIds) {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(affectedNodeIds);
            context.SetMetadata(node, new IncrementalAnalysisNodeFilterMetadata(affectedNodeIds));
        }
    }
}