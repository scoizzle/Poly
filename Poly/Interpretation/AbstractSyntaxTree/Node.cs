namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Base class for abstract syntax tree nodes.
/// Nodes are pure data structures with no semantic responsibility.
/// Type information is resolved by semantic analysis middleware.
/// Transformation is handled entirely by middleware in the interpretation pipeline.
/// </summary>
public abstract record Node
{
    public virtual IEnumerable<Node?> Children => Enumerable.Empty<Node>();
}