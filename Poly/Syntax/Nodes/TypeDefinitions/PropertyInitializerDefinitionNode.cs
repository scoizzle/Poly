namespace Poly.Syntax.Nodes;

/// <summary>
/// AST node representing a property initializer.
/// </summary>
/// <param name="Value">The initializer value expression.</param>
public sealed record PropertyInitializerDefinitionNode(Node Value) : Node {
    public override IEnumerable<Node?> Children => [Value];

    public override string ToString() => $"= {Value}";
}