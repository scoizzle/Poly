namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a logical AND operation (short-circuiting) between two boolean values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.AndAlso"/> which implements short-circuit evaluation:
/// if the left operand is false, the right operand is not evaluated.
/// Corresponds to the <c>&amp;&amp;</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record And(Node LeftHandValue, Node RightHandValue) : Expression {
    /// <inheritdoc />
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];
    /// <inheritdoc />
    public override string ToString() => $"{LeftHandValue} and {RightHandValue}";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) =>
        EmitBinaryOp(LeftHandValue, RightHandValue, Primitives.OpKind.And, context);
}