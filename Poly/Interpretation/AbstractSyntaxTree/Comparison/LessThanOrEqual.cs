namespace Poly.Interpretation.AbstractSyntaxTree.Comparison;

/// <summary>
/// Represents a less-than-or-equal comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.LessThanOrEqual"/> which tests if the left value is less than or equal to the right value.
/// Corresponds to the <c>&lt;=</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record LessThanOrEqual(Node LeftHandValue, Node RightHandValue) : BooleanOperator
{
    public override string ToString() => $"{LeftHandValue} <= {RightHandValue}";
}