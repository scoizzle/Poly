namespace Poly.Syntax.AbstractSyntaxTree.TypeDefinitions;

/// <summary>
/// AST node representing a union/sum type.
/// </summary>
/// <param name="Options">The candidate types in this union.</param>
public sealed record UnionTypeReference(
    IReadOnlyList<Node> Options
) : Node {

    public override IEnumerable<Node?> Children => Options;

    public override string ToString() => string.Join(" | ", Options.Select(static option => option.ToString()));
}