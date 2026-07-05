namespace Poly.Syntax.Nodes;

public sealed record SuspendNode(Node Inner, string? Reason = null) : Expression {
    public override IEnumerable<Node?> Children => [Inner];

    public override string ToString() => $"suspend({Inner})";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        // Suspend is a runtime signal; lower to the inner expression for now
        foreach (var p in Inner.ToPrimitives(context)) yield return p;
    }
}