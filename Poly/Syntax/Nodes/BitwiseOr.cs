namespace Poly.Syntax.Nodes;

public sealed record BitwiseOr(Node LeftHandValue, Node RightHandValue) : Expression {
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];
    public override string ToString() => $"({LeftHandValue} | {RightHandValue})";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) =>
        EmitBinaryOp(LeftHandValue, RightHandValue, Primitives.OpKind.Or, context);
}