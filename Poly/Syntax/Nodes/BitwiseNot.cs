namespace Poly.Syntax.Nodes;

public sealed record BitwiseNot(Node Operand) : Expression {
    public override IEnumerable<Node?> Children => [Operand];
    public override string ToString() => $"~{Operand}";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        foreach (var p in Operand.ToPrimitives(context)) yield return p;
        yield return new Poly.Syntax.Primitives.UnaryOp(Poly.Syntax.Primitives.UnaryOpKind.BitNot);
    }
}