namespace Poly.Interpretation.AbstractSyntaxTree.Arithmetic;

/// <summary>
/// Represents an subtraction operation between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Subtract"/> which performs numeric subtraction.
/// Corresponds to the <c>-</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Subtract(Node LeftHandValue, Node RightHandValue) : Operator
{
    /// <inheritdoc />
    public override TResult Transform<TResult>(ITransformer<TResult> transformer) => transformer.Transform(this);

    /// <inheritdoc />
    public override string ToString() => $"({LeftHandValue} - {RightHandValue})";
}