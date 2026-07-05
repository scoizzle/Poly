namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a using statement that manages resource disposal.
/// </summary>
/// <remarks>
/// The resource is acquired and the body is executed. Regardless of how the body completes,
/// the resource is released (via cleanup operations specific to the implementation language).
/// This pattern ensures deterministic resource management.
/// </remarks>
public sealed record UsingStatement(Node Resource, Node Body) : Statement {
    public override IEnumerable<Node?> Children => [Resource, Body];

    /// <inheritdoc />
    public override string ToString() => $"using ({Resource}) {{ {Body} }}";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        // Using: evaluate resource, execute body, then call Dispose via finally pattern.
        // Without exception handling primitives, we emit: resource, discard, body.
        foreach (var p in Resource.ToPrimitives(context)) yield return p;
        yield return new Primitives.Discard();
        foreach (var p in Body.ToPrimitives(context)) yield return p;
    }
}