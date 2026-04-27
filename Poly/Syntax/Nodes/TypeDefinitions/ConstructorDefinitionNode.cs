namespace Poly.Syntax.Nodes;

/// <summary>
/// AST node representing a constructor definition on a type.
/// </summary>
/// <param name="Parameters">The constructor parameters.</param>
/// <param name="Body">Optional constructor body as an AST node.</param>
public sealed record ConstructorDefinitionNode(
    IReadOnlyList<Parameter>? Parameters = null,
    Node? Body = null
) : Node {

    public override IEnumerable<Node?> Children {
        get {
            if (Parameters != null)
                foreach (var parameter in Parameters) yield return parameter;
            yield return Body;
        }
    }

    public override string ToString() {
        var parameterList = Parameters != null ? string.Join(", ", Parameters) : string.Empty;
        return $"ctor({parameterList})";
    }
}