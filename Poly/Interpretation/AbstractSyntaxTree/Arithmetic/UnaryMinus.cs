namespace Poly.Interpretation.AbstractSyntaxTree.Arithmetic;

/// <summary>
/// Represents a unary negation operation that negates a numeric value.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Negate"/> which returns the arithmetic negation of the operand.
/// Corresponds to the <c>-</c> prefix operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record UnaryMinus(Node Operand) : Operator
{
    /// <inheritdoc />
    public override TResult Transform<TResult>(ITransformer<TResult> transformer) => transformer.Transform(this);

    /// <inheritdoc />
    public override string ToString() => $"-{Operand}";
}