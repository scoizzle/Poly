namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a method invocation operation in an interpretation tree.
/// </summary>
/// <remarks>
/// Type information is resolved by semantic analysis middleware.
/// Method overload resolution happens in middleware using type information from the context,
/// not on the node itself.
/// </remarks>
public sealed record MethodInvocation(Node Target, string MethodName, params Node[] Arguments) : Operator
{
    /// <inheritdoc />
    public override TResult Transform<TResult>(ITransformer<TResult> transformer) => transformer.Transform(this);
}