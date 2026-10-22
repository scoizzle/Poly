namespace Poly.Analysis;

public interface INodeAnalyzer {
    void Analyze(AnalysisContext context, Node node);

    /// <summary>Stable display name for this pass, used in telemetry and diagnostics.
    /// Each concrete pass should expose a <c>public const string Id</c>.</summary>
    string PassName { get; }
}

public static class NodeAnalyzerExtensions {
    public static void AnalyzeChildren(this INodeAnalyzer analyzer, AnalysisContext context, Node node) {
        foreach (var child in node.Children) {
            if (child is null)
                continue;

            analyzer.Analyze(context, child);
        }
    }

    /// <summary>
    /// Recursively analyzes all children (via the standard dispatch) while aggregating
    /// a value computed from each child.
    /// 
    /// The <paramref name="childSelector"/> is called for each child after (or as part of)
    /// ensuring the child has been analyzed. It should return the value for that child
    /// (often by calling Analyze on the child and then reading metadata or computing a property).
    /// 
    /// This ensures a *single* traversal of the Children enumerable for both visitation
    /// and aggregation, avoiding a separate post-pass that re-walks children.
    /// 
    /// Use direct indexed access on concrete collections (e.g. block.Nodes) inside your
    /// Analyze override for hot paths with position-dependent logic or very wide nodes.
    /// </summary>
    public static T AggregateChildren<T>(
        this INodeAnalyzer _,
        AnalysisContext context,
        Node node,
        Func<AnalysisContext, Node, T> childSelector,
        Func<T, T, T> combiner,
        T identity) {
        T result = identity;
        foreach (var child in node.Children) {
            if (child is null)
                continue;

            T value = childSelector(context, child);
            result = combiner(result, value);
        }
        return result;
    }

    /// <summary>
    /// Convenience for the common "does any child subtree satisfy a predicate" case
    /// (short-circuits on first true). The walk descends until the predicate holds; the
    /// given node itself is never tested, only its descendants.
    ///
    /// Two node kinds are traversal barriers: a <see cref="Lambda"/> body and a
    /// <see cref="TypeDefinitionNode"/> body are separate scopes, so a match inside one is
    /// not a match at the enclosing declaration. The barrier node itself is still tested;
    /// the walk just does not enter it.
    ///
    /// No visited set is needed: <see cref="Node"/> is a DAG of records, so a subtree
    /// cannot cycle.
    /// </summary>
    public static bool AnyChild<TMetadata>(
        this INodeAnalyzer _,
        AnalysisContext context,
        Node node,
        Func<AnalysisContext, Node, bool> predicate)
        where TMetadata : class, IAnalysisMetadata =>
        AnyDescendant(context, node, predicate);

    private static bool AnyDescendant(
        AnalysisContext context,
        Node node,
        Func<AnalysisContext, Node, bool> predicate) {
        foreach (var child in node.Children) {
            if (child is null)
                continue;

            if (predicate(context, child))
                return true;

            if (child is Lambda or TypeDefinitionNode)
                continue;

            if (AnyDescendant(context, child, predicate))
                return true;
        }
        return false;
    }
}