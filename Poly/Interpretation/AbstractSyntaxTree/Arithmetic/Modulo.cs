namespace Poly.Interpretation.AbstractSyntaxTree.Arithmetic;

/// <summary>
/// Represents an modulo operation between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Modulo"/> which performs numeric modulo.
/// Corresponds to the <c>%</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Modulo(Node LeftHandValue, Node RightHandValue) : Operator
{
    /// <inheritdoc />
    public override string ToString() => $"({LeftHandValue} % {RightHandValue})";
}