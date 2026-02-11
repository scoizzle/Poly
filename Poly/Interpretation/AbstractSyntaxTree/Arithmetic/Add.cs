namespace Poly.Interpretation.AbstractSyntaxTree.Arithmetic;

/// <summary>
/// Represents an addition operation between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Add"/> which performs numeric addition.
/// Corresponds to the <c>+</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Add(Node LeftHandValue, Node RightHandValue) : Operator {
    /// <inheritdoc />
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];
    
    /// <inheritdoc />
    public override string ToString() => $"({LeftHandValue} + {RightHandValue})";
}