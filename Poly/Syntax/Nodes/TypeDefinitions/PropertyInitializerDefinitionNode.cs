namespace Poly.Ast.Nodes;

/// <summary>
/// AST node representing a property initializer.
/// </summary>
/// <param name="Value">The initializer value expression.</param>
/// <param name="AccessModifier">Optional access modifier for the initializer (e.g., private).</param>
public sealed record PropertyInitializerDefinitionNode(
    Node Value,
    AccessModifier? AccessModifier = null
) : Node {
    public override IEnumerable<Node?> Children => [Value];

    public override string ToString() => $"= {Value}";
}