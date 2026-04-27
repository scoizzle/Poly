namespace Poly.Syntax.Nodes;

/// <summary>
/// AST node representing an optional/nullable type wrapper.
/// </summary>
/// <param name="InnerType">The inner type that is optional.</param>
public sealed record OptionalTypeReference(
    Node InnerType
) : Node {

    public override IEnumerable<Node?> Children => [InnerType];

    public override string ToString() => $"{InnerType}?";
}