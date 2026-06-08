namespace Poly.Syntax;

/// <summary>
/// Base class for abstract syntax tree nodes.
/// Nodes are pure data structures with no semantic responsibility.
/// Type information is resolved by semantic analysis passes.
/// Each node has a stable identifier for metadata storage and incremental analysis.
/// </summary>
public abstract record Node {
    /// <summary>
    /// Stable identifier for this node.
    /// Preserved across parser runs for the same source location/structure.
    /// Used as key for metadata storage and caching.
    /// </summary>
    public NodeId Id { get; init; } = NodeId.NewId();

    /// <summary>
    /// Owned child nodes of this node.
    /// </summary>
    public virtual IEnumerable<Node?> Children => [];

    /// <summary>
    /// Compact representation for VM trace output.
    /// Override to provide a shorter description than <see cref="object.ToString"/>.
    /// </summary>
    public virtual string ToTraceString() => ToString() ?? GetType().Name;
}