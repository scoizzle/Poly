namespace Poly.Syntax.Analysis;

public sealed record NodeChange(
    NodeId NodeId,
    string NodeType,
    string NodeName,
    string BeforeFingerprint,
    string AfterFingerprint,
    IReadOnlyList<Diagnostic> RelatedDiagnostics
);

public sealed record NodeSnapshot(
    NodeId NodeId,
    string NodeType,
    string NodeName,
    string Fingerprint
);

public sealed record NodeSnapshotSet(
    string RootName,
    IReadOnlyList<NodeSnapshot> Nodes
);

public sealed record NodeDiffReport(
    IReadOnlyList<NodeSnapshot> Added,
    IReadOnlyList<NodeSnapshot> Removed,
    IReadOnlyList<NodeChange> Changed
);

public static class SyntaxDiffUtil {
    public static NodeSnapshotSet CaptureSnapshot(
        Node root,
        Func<Node, string> getNodeName,
        Func<Node, string> buildFingerprint,
        Func<Node, IEnumerable<Node?>>? getChildren = null)
        => CaptureSnapshot(root, root.GetType().Name, getNodeName, buildFingerprint, getChildren);

    public static NodeSnapshotSet CaptureSnapshot(
        Node root,
        string rootName,
        Func<Node, string> getNodeName,
        Func<Node, string> buildFingerprint,
        Func<Node, IEnumerable<Node?>>? getChildren = null) {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(rootName);
        ArgumentNullException.ThrowIfNull(getNodeName);
        ArgumentNullException.ThrowIfNull(buildFingerprint);

        var nodes = Flatten(root, getNodeName, buildFingerprint, getChildren)
            .OrderBy(static entry => entry.NodeType, StringComparer.Ordinal)
            .ThenBy(static entry => entry.NodeName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.NodeId.Value, StringComparer.Ordinal)
            .ToArray();

        return new NodeSnapshotSet(rootName, nodes);
    }

    public static NodeDiffReport Compare(
        Node before,
        Node after,
        Func<Node, string> getNodeName,
        Func<Node, string> buildFingerprint,
        AnalysisResult? analysis = null,
        Func<Node, IEnumerable<Node?>>? getChildren = null) {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var beforeSnapshot = CaptureSnapshot(before, getNodeName, buildFingerprint, getChildren);
        var afterSnapshot = CaptureSnapshot(after, getNodeName, buildFingerprint, getChildren);

        return CompareSnapshots(beforeSnapshot, afterSnapshot, analysis);
    }

    public static NodeDiffReport CompareSnapshots(NodeSnapshotSet before, NodeSnapshotSet after, AnalysisResult? analysis = null) {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var beforeMap = before.Nodes.ToDictionary(static entry => entry.NodeId);
        var afterMap = after.Nodes.ToDictionary(static entry => entry.NodeId);
        var diagnosticsByNode = analysis?.Diagnostics
            .GroupBy(static diagnostic => diagnostic.Node.Id)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Diagnostic>)group.ToArray())
            ?? new Dictionary<NodeId, IReadOnlyList<Diagnostic>>();

        var added = afterMap
            .Where(kvp => !beforeMap.ContainsKey(kvp.Key))
            .Select(static kvp => kvp.Value)
            .OrderBy(static entry => entry.NodeType, StringComparer.Ordinal)
            .ThenBy(static entry => entry.NodeName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.NodeId.Value, StringComparer.Ordinal)
            .ToArray();

        var removed = beforeMap
            .Where(kvp => !afterMap.ContainsKey(kvp.Key))
            .Select(static kvp => kvp.Value)
            .OrderBy(static entry => entry.NodeType, StringComparer.Ordinal)
            .ThenBy(static entry => entry.NodeName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.NodeId.Value, StringComparer.Ordinal)
            .ToArray();

        var changed = afterMap
            .Where(kvp => beforeMap.TryGetValue(kvp.Key, out var old) && !string.Equals(old.Fingerprint, kvp.Value.Fingerprint, StringComparison.Ordinal))
            .Select(kvp => {
                var old = beforeMap[kvp.Key];
                var current = kvp.Value;
                return new NodeChange(
                    current.NodeId,
                    current.NodeType,
                    current.NodeName,
                    old.Fingerprint,
                    current.Fingerprint,
                    diagnosticsByNode.TryGetValue(current.NodeId, out var diagnostics) ? diagnostics : []);
            })
            .OrderBy(static entry => entry.NodeType, StringComparer.Ordinal)
            .ThenBy(static entry => entry.NodeName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.NodeId.Value, StringComparer.Ordinal)
            .ToArray();

        return new NodeDiffReport(added, removed, changed);
    }

    private static IEnumerable<NodeSnapshot> Flatten(
        Node root,
        Func<Node, string> getNodeName,
        Func<Node, string> buildFingerprint,
        Func<Node, IEnumerable<Node?>>? getChildren) {
        var visited = new HashSet<NodeId>();

        foreach (var node in Traverse(root, getChildren)) {
            if (!visited.Add(node.Id)) {
                continue;
            }

            yield return new NodeSnapshot(
                node.Id,
                node.GetType().Name,
                getNodeName(node),
                buildFingerprint(node));
        }
    }

    private static IEnumerable<Node> Traverse(Node root, Func<Node, IEnumerable<Node?>>? getChildren) {
        var stack = new Stack<Node>();
        stack.Push(root);

        while (stack.Count > 0) {
            var current = stack.Pop();
            yield return current;

            var children = getChildren?.Invoke(current) ?? current.Children;
            foreach (var child in children.Reverse()) {
                if (child is not null) {
                    stack.Push(child);
                }
            }
        }
    }
}