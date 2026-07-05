namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents an implicit reference to the current instance inside an instance member body.
/// </summary>
public sealed record ThisReference : Expression {
    public override string ToString() => "this";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        // this resolves to a 0-based handle; in practice the resolved type
        // metadata provides the actual slot or heap reference
        yield return new Primitives.PushConstant(0L);
    }
}