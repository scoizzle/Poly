namespace Poly.Ast.Nodes;

/// <summary>
/// AST node representing a property initializer.
///
/// A property whose <see cref="PropertyDefinitionNode.Setter"/> is null but whose
/// <see cref="PropertyDefinitionNode.Initializer"/> is present renders an init-only
/// accessor (<c>{ get; init; }</c>): the property is set through the initializer, not a
/// setter. When <see cref="Value"/> is non-null an initializer expression is appended
/// (<c>{ get; init; } = value;</c>); when null only the accessor is emitted.
/// </summary>
/// <param name="Value">The initializer value expression, or null for a bare init accessor.</param>
/// <param name="AccessModifier">Optional access modifier for the initializer (e.g., private).</param>
public sealed record PropertyInitializerDefinitionNode(
    Node? Value = null,
    AccessModifier? AccessModifier = null
) : Node {
    public override IEnumerable<Node?> Children => Value is null ? [] : [Value];

    public override string ToString() => Value is null ? "init" : $"= {Value}";
}