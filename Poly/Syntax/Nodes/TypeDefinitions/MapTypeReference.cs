namespace Poly.Syntax.Nodes;

/// <summary>
/// AST node representing a map/dictionary type.
/// </summary>
/// <param name="KeyType">The key type.</param>
/// <param name="ValueType">The value type.</param>
public sealed record MapTypeReference(
    Node KeyType,
    Node ValueType
) : Node {

    public override IEnumerable<Node?> Children => [KeyType, ValueType];

    public override string ToString() => $"Map<{KeyType}, {ValueType}>";
}