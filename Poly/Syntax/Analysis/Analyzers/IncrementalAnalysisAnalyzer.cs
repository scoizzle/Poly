namespace Poly.Syntax.Analysis;

internal readonly record struct NodeRange(int StartInclusive, int EndExclusive);

internal sealed record IncrementalAnalysisTreeIndex(
    IReadOnlyDictionary<NodeId, NodeId> ParentByNodeId,
    IReadOnlyDictionary<NodeId, NodeRange> SubtreeRangeByNodeId,
    IReadOnlyList<NodeId> PreorderNodeIds
) : IAnalysisMetadata;

public sealed class IncrementalAnalysisAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        var treeIndex = context.GetIncrementalAnalysisTreeIndex();
        if (treeIndex is null) {
            BuildTreeIndex(context, node);
            return;
        }

        var invalidatedNodes = context.GetInvalidatedNodes();
        if (invalidatedNodes is null) {
            return;
        }

        BuildAnalysisFilterList(context, node, treeIndex, invalidatedNodes);
    }

    private static void BuildAnalysisFilterList(AnalysisContext context, Node node, IncrementalAnalysisTreeIndex treeIndex, IEnumerable<Node> invalidatedNodes) {
        var affectedNodeIds = GetAffectedNodes().ToHashSet();
        affectedNodeIds.Add(node.Id);

        foreach (var invalidatedNodeId in affectedNodeIds) {
            context.ClearMetadata(invalidatedNodeId);
            context.ClearDiagnostics(invalidatedNodeId);
        }

        // Rebuild the index so the next incremental pass sees the post-mutation tree shape.
        BuildTreeIndex(context, node);

        context.SetAffectedNodesForIncrementalAnalysis(affectedNodeIds);

        IEnumerable<NodeId> GetAffectedNodes() {
            foreach (var node in invalidatedNodes) {
                if (treeIndex.SubtreeRangeByNodeId.TryGetValue(node.Id, out var subtreeRange)) {
                    for (var i = subtreeRange.StartInclusive; i < subtreeRange.EndExclusive; i++) {
                        yield return treeIndex.PreorderNodeIds[i];
                    }
                }

                var descendants = new Stack<Node>();
                descendants.Push(node);

                while (descendants.Count > 0) {
                    var current = descendants.Pop();
                    yield return current.Id;

                    foreach (var child in current.Children) {
                        if (child is null) {
                            continue;
                        }

                        descendants.Push(child);
                    }
                }

                var currentId = node.Id;
                while (treeIndex.ParentByNodeId.TryGetValue(currentId, out var nextParentId)) {
                    yield return nextParentId;
                    currentId = nextParentId;
                }
            }
        }
    }

    private static void BuildTreeIndex(AnalysisContext context, Node node) {
        var parentByNodeId = new Dictionary<NodeId, NodeId>();
        var subtreeRanges = new Dictionary<NodeId, NodeRange>();
        var preorderNodeIds = new List<NodeId>();

        Traverse(parentByNodeId, subtreeRanges, preorderNodeIds, node, parentId: null);

        context.SetIncrementalAnalysisTreeIndex(new IncrementalAnalysisTreeIndex(parentByNodeId, subtreeRanges, preorderNodeIds));
        return;

        static void Traverse(
            Dictionary<NodeId, NodeId> parentByNodeId,
            Dictionary<NodeId, NodeRange> subtreeRanges,
            List<NodeId> preorderNodeIds,
            Node current,
            NodeId? parentId) {
            if (parentId.HasValue && !parentByNodeId.TryAdd(current.Id, parentId.Value)) {
                return;
            }

            var startIndex = preorderNodeIds.Count;
            preorderNodeIds.Add(current.Id);

            foreach (var child in current.Children) {
                if (child is null)
                    continue;

                Traverse(parentByNodeId, subtreeRanges, preorderNodeIds, child, current.Id);
            }

            subtreeRanges[current.Id] = new NodeRange(startIndex, preorderNodeIds.Count);
        }
    }
}

public static class IncrementalAnalysisAnalyzerExtensions {
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
        public bool IsIncrementalAnalysisAvailable() {
            return context.GetMetadata<IncrementalAnalysisTreeIndex>(default) is not null;
        }

        internal IncrementalAnalysisTreeIndex? GetIncrementalAnalysisTreeIndex() {
            return context.GetMetadata<IncrementalAnalysisTreeIndex>(default);
        }

        internal IEnumerable<Node>? GetInvalidatedNodes() {
            return context.GetMetadata<IncrementalAnalysisMutationMetadata>(default)?.InvalidatedNodes;
        }

        public bool ShouldAnalyze(Node node) {
            ArgumentNullException.ThrowIfNull(node);
            return context.GetMetadata<IncrementalAnalysisNodeFilterMetadata>(default) is not IncrementalAnalysisNodeFilterMetadata filterMetadata
                || filterMetadata.AffectedNodeIds.Contains(node.Id);
        }
    }

    extension(AnalysisContext context) {
        internal void SetIncrementalAnalysisTreeIndex(IncrementalAnalysisTreeIndex treeIndex) {
            ArgumentNullException.ThrowIfNull(treeIndex);
            context.SetMetadata(default, treeIndex);
        }

        public void SetInvalidatedNodesForIncrementalAnalysis(IEnumerable<Node> invalidatedNodes) {
            ArgumentNullException.ThrowIfNull(invalidatedNodes);
            context.SetMetadata(default, new IncrementalAnalysisMutationMetadata(invalidatedNodes));
        }

        public void SetAffectedNodesForIncrementalAnalysis(IReadOnlyCollection<NodeId> affectedNodeIds) {
            ArgumentNullException.ThrowIfNull(affectedNodeIds);
            context.SetMetadata(default, new IncrementalAnalysisNodeFilterMetadata(affectedNodeIds));
        }
    }
}