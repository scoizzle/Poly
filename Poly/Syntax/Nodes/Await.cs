namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents an await expression that extracts the result from an awaitable operation.
/// </summary>
public sealed record Await(Node Operand) : Expression {
    public override IEnumerable<Node?> Children => [Operand];

    public override string ToString() => $"await {Operand}";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        foreach (var p in Operand.ToPrimitives(context)) yield return p;
    }
}