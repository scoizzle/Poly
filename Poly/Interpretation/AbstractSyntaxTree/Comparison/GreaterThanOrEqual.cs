namespace Poly.Interpretation.AbstractSyntaxTree.Comparison;

/// <summary>
/// Represents a greater-than-or-equal comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.GreaterThanOrEqual"/> which tests if the left value is greater than or equal to the right value.
/// Corresponds to the <c>&gt;=</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record GreaterThanOrEqual(Node LeftHandValue, Node RightHandValue) : BooleanOperator
{
    public override string ToString() => $"{LeftHandValue} >= {RightHandValue}";
}