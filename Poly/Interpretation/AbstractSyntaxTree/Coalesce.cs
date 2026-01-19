namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a null-coalescing operation that returns the left-hand value if it's not null, otherwise returns the right-hand value.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Coalesce"/> which evaluates the left operand and returns it if non-null,
/// otherwise evaluates and returns the right operand.
/// Corresponds to the <c>??</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Coalesce(Node LeftHandValue, Node RightHandValue) : Operator
{
    /// <inheritdoc />
    public override TResult Transform<TResult>(ITransformer<TResult> transformer) => transformer.Transform(this);

    /// <inheritdoc />
    public override string ToString() => $"({LeftHandValue} ?? {RightHandValue})";
}