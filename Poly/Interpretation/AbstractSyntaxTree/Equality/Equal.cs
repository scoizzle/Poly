namespace Poly.Interpretation.AbstractSyntaxTree.Equality;

/// <summary>
/// Represents an equality comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Equal"/> which tests if two values are equal.
/// Corresponds to the <c>==</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Equal(Node LeftHandValue, Node RightHandValue) : BooleanOperator
{
    /// <inheritdoc />
    public override TResult Transform<TResult>(ITransformer<TResult> transformer) => transformer.Transform(this);

    /// <inheritdoc />
    public override string ToString() => $"{LeftHandValue} == {RightHandValue}";
}