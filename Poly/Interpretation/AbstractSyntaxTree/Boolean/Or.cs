namespace Poly.Interpretation.AbstractSyntaxTree.Boolean;

/// <summary>
/// Represents a logical OR operation (short-circuiting) between two boolean values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.OrElse"/> which implements short-circuit evaluation:
/// if the left operand is true, the right operand is not evaluated.
/// Corresponds to the <c>||</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Or(Node LeftHandValue, Node RightHandValue) : BooleanOperator
{
    /// <inheritdoc />
    public override TResult Transform<TResult>(ITransformer<TResult> transformer) => transformer.Transform(this);

    /// <inheritdoc />
    public override string ToString() => $"{LeftHandValue} || {RightHandValue}";
}