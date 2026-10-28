namespace Poly.Analysis;

static class NodeReplacementMetadataExtensions {
    /// <summary>
    /// Analysis metadata that substitutes a node with a lowered equivalent during backend processing.
    /// Register via <c>context.SetNodeReplacement()</c> in an <see cref="INodeAnalyzer"/> pass to have
    /// backends transparently compile the replacement instead of the original node.
    /// </summary>
    /// <param name="Replacement">The node that replaces the original in the backend pipeline.</param>
    private sealed record NodeReplacementMetadata(Node Replacement) : IAnalysisMetadata;

    extension(AnalysisContext context) {
        /// <summary>
        /// Registers a node replacement so that backends substitute <paramref name="replacement"/>
        /// in place of <paramref name="node"/> during code generation.
        /// </summary>
        public void SetNodeReplacement(Node node, Node replacement) {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(replacement);

            context.Metadata.Set(node, new NodeReplacementMetadata(replacement));
        }
    }

    extension(INodeMetadataProvider provider) {
        /// <summary>
        /// Gets the replacement node registered for <paramref name="node"/>, or null if none.
        /// </summary>
        public Node? GetNodeReplacement(Node node) {
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(node);

            return provider.GetMetadata<NodeReplacementMetadata>(node)?.Replacement;
        }

        public Node GetEffectiveNode(Node node) {
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(node);

            while (node is not null && provider.GetNodeReplacement(node) is Node replacement)
                node = replacement;

            return node!;
        }
    }
}