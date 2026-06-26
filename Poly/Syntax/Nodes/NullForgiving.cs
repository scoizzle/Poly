namespace Poly.Syntax.Nodes;

public sealed record NullForgiving(Node Operand) : Expression {
    public override IEnumerable<Node?> Children => [Operand];

    public override string ToString() => $"{Operand}!";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        // Null-forgiving ! is a compile-time hint; at runtime it's a no-op passthrough
        foreach (var p in Operand.ToPrimitives(context)) yield return p;
    }
}