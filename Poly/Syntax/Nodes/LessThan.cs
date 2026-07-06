namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a less-than comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.LessThan"/> which tests if the left value is less than the right value.
/// Corresponds to the <c>&lt;</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record LessThan(Node LeftHandValue, Node RightHandValue) : Expression {
    /// <inheritdoc />
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];

    public override string ToString() => $"{LeftHandValue} < {RightHandValue}";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) =>
        EmitBinaryOp(LeftHandValue, RightHandValue, Primitives.OpKind.Lt, context);
}