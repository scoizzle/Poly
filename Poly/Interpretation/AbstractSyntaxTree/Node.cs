namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Base class for abstract syntax tree nodes.
/// Nodes are pure data structures with no semantic responsibility.
/// Type information is resolved by semantic analysis passes (INodeAnalyzer implementations).
/// Each node has a stable identifier for metadata storage and incremental analysis.
/// </summary>
public abstract record Node {
    /// <summary>
    /// Stable identifier for this node.
    /// Preserved across parser runs for the same source location/structure.
    /// Used as key for metadata storage and caching.
    /// </summary>
    public NodeId Id { get; init; } = NodeId.NewId();

    public virtual IEnumerable<Node?> Children => Enumerable.Empty<Node>();
}