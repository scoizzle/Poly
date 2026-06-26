namespace Poly.Syntax.Nodes;

[DebuggerDisplay("{TypeName}")]
public record TypeReference(string TypeName) : Node {
    public static TypeReference To<T>() => new ClrTypeReference(typeof(T));

    public override string ToString() => TypeName;

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        // Type references are compile-time metadata only
        yield return new Poly.Syntax.Primitives.PushConstant(0L);
    }
};