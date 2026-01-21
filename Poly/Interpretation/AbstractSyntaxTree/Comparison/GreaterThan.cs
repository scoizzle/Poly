namespace Poly.Interpretation.AbstractSyntaxTree.Comparison;

/// <summary>
/// Represents a greater-than comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.GreaterThan"/> which tests if the left value is greater than the right value.
/// Corresponds to the <c>&gt;</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record GreaterThan(Node LeftHandValue, Node RightHandValue) : BooleanOperator
{
    public override string ToString() => $"{LeftHandValue} > {RightHandValue}";
}