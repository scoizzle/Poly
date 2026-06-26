namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a member access operation (property, field, or method access) in an interpretation tree.
/// </summary>
/// <remarks>
/// This operator enables accessing members of a value using dot notation (e.g., <c>person.Name</c>).
/// Member resolution happens in semantic analysis passes (INodeAnalyzer implementations) using type information from the context.
/// </remarks>
public sealed record Member(Node Value, string MemberName) : Expression {
    public override IEnumerable<Node?> Children => [Value];

    /// <inheritdoc />
    public override string ToString() => $"{Value}.{MemberName}";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        // Member access resolves to a slot or heap dereference via analysis.
        // Without resolved member metadata, passthrough the value.
        foreach (var p in Value.ToPrimitives(context)) yield return p;
        yield return new Poly.Syntax.Primitives.PushConstant(0L);
    }
}