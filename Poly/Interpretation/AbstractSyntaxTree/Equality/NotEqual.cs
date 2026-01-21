namespace Poly.Interpretation.AbstractSyntaxTree.Equality;

/// <summary>
/// Represents an inequality comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.NotEqual"/> which tests if two values are not equal.
/// Corresponds to the <c>!=</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record NotEqual(Node LeftHandValue, Node RightHandValue) : BooleanOperator
{
    public override string ToString() => $"{LeftHandValue} != {RightHandValue}";
}