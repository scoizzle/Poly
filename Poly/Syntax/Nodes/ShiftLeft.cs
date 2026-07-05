namespace Poly.Syntax.Nodes;

public sealed record ShiftLeft(Node LeftHandValue, Node RightHandValue) : Expression {
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];
    public override string ToString() => $"({LeftHandValue} << {RightHandValue})";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        foreach (var p in LeftHandValue.ToPrimitives(context)) yield return p;
        foreach (var p in RightHandValue.ToPrimitives(context)) yield return p;
        yield return new Primitives.BinaryOp(Poly.Syntax.Primitives.OpKind.Shl);
    }
}