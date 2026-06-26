namespace Poly.Syntax.Nodes;

/// <summary>Represents a new array creation expression: <c>new T[size]</c>.
/// The element type and length are both explicit nodes.</summary>
/// <param name="ElementType">The element type (a <see cref="TypeReference"/> or
/// <see cref="TypeDefinitionReference"/>).</param>
/// <param name="Length">The length expression.</param>
public sealed record NewArray(Node ElementType, Node Length) : Expression {
    public override IEnumerable<Node?> Children => [Length, ElementType];
    public override string ToString() => $"new {ElementType}[{Length}]";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        // Discard element type (metadata only), emit length, allocate
        foreach (var p in ElementType.ToPrimitives(context)) yield return p;
        yield return new Poly.Syntax.Primitives.Discard();
        foreach (var p in Length.ToPrimitives(context)) yield return p;
        yield return new Poly.Syntax.Primitives.NewArray();
    }
}