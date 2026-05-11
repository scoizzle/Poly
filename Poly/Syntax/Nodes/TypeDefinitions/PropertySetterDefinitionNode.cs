namespace Poly.Syntax.Nodes;

/// <summary>
/// AST node representing a property setter body.
/// </summary>
/// <param name="ValueParameter">The implicit value parameter exposed to the setter body.</param>
/// <param name="Body">The setter body.</param>
/// <param name="AccessModifier">Optional access modifier for the setter (e.g., private).</param>
public sealed record PropertySetterDefinitionNode(
    Parameter? ValueParameter = null,
    Node? Body = null,
    AccessModifier? AccessModifier = null
) : Node {
    public override IEnumerable<Node?> Children => [ValueParameter, Body];

    public override string ToString() => Body is null ? "set;" : $"set({ValueParameter?.Name ?? "value"}) => {Body}";
}