namespace Poly.Interpretation.AbstractSyntaxTree.Boolean;

/// <summary>
/// Represents a logical AND operation (short-circuiting) between two boolean values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.AndAlso"/> which implements short-circuit evaluation:
/// if the left operand is false, the right operand is not evaluated.
/// Corresponds to the <c>&amp;&amp;</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record And(Node LeftHandValue, Node RightHandValue) : BooleanOperator
{
    /// <inheritdoc />
    public override string ToString() => $"{LeftHandValue} and {RightHandValue}";
}