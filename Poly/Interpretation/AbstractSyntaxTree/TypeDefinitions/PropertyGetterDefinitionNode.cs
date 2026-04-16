namespace Poly.Interpretation.AbstractSyntaxTree.TypeDefinitions;

/// <summary>
/// AST node representing a property getter body.
/// </summary>
/// <param name="Body">The getter body.</param>
public sealed record PropertyGetterDefinitionNode(Node? Body = null) : Node {
    public override IEnumerable<Node?> Children => [Body];

    public override string ToString() => Body is null ? "get;" : $"get => {Body}";
}