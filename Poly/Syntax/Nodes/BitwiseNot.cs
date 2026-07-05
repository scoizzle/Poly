namespace Poly.Syntax.Nodes;

public sealed record BitwiseNot(Node Operand) : Expression {
    public override IEnumerable<Node?> Children => [Operand];
    public override string ToString() => $"~{Operand}";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        foreach (var p in Operand.ToPrimitives(context)) yield return p;
        yield return new Primitives.UnaryOp(Poly.Syntax.Primitives.UnaryOpKind.BitNot);
    }
}