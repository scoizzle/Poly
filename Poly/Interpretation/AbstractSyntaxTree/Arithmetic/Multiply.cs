namespace Poly.Interpretation.AbstractSyntaxTree.Arithmetic;

/// <summary>
/// Represents an multiplication operation between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Multiply"/> which performs numeric multiplication.
/// Corresponds to the <c>*</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Multiply(Node LeftHandValue, Node RightHandValue) : Operator
{
    /// <inheritdoc />
    public override string ToString() => $"({LeftHandValue} * {RightHandValue})";
}