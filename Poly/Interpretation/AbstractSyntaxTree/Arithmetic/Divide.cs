namespace Poly.Interpretation.AbstractSyntaxTree.Arithmetic;

/// <summary>
/// Represents an division operation between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Divide"/> which performs numeric division.
/// Corresponds to the <c>/</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Divide(Node LeftHandValue, Node RightHandValue) : Operator
{
    /// <inheritdoc />
    public override TResult Transform<TResult>(ITransformer<TResult> transformer) => transformer.Transform(this);

    /// <inheritdoc />
    public override string ToString() => $"({LeftHandValue} / {RightHandValue})";
}