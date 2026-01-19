namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Base class for abstract syntax tree nodes.
/// Nodes are pure data structures with no semantic responsibility.
/// Type information is resolved by semantic analysis middleware, not by nodes themselves.
/// </summary>
/// <remarks>
/// This hierarchy represents the abstract syntax of expression trees in an immutable, decoupled form.
/// Nodes participate in the middleware interpretation pipeline via the <see cref="Transform{TResult}"/> visitor pattern,
/// delegating to <see cref="ITransformer{TResult}"/> implementations for domain-specific transformations.
/// </remarks>
public abstract record Node
{
    /// <summary>
    /// Transforms this node using the provided transformer.
    /// Type information is resolved by semantic analysis middleware, not by the node itself.
    /// </summary>
    /// <typeparam name="TResult">The type of the transformation result.</typeparam>
    /// <param name="transformer">The transformer to apply to this node.</param>
    /// <returns>The transformation result.</returns>
    public abstract TResult Transform<TResult>(ITransformer<TResult> transformer);
}