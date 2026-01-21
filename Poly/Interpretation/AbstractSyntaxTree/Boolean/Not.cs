namespace Poly.Interpretation.AbstractSyntaxTree.Boolean;

/// <summary>
/// Represents a logical NOT operation (negation) of a boolean value.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Not"/> which inverts the boolean value.
/// Corresponds to the <c>!</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Not(Node Value) : BooleanOperator
{
    /// <inheritdoc />
    public override string ToString() => $"!{Value}";
}