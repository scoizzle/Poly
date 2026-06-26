namespace Poly.Syntax.Nodes;

public sealed record Default(Node? TargetType = null) : Expression {
    public override string ToString() => TargetType is not null ? $"default({TargetType})" : "default";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        // Default value is always 0 at the µop level. Type-aware defaults
        // (null for ref types) would need resolved type metadata from analysis.
        yield return new Poly.Syntax.Primitives.PushConstant(0L);
    }
}