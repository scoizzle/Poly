namespace Poly.Syntax.Nodes;

/// <summary>
/// AST node representing a constructor definition on a type.
/// </summary>
/// <param name="Parameters">The constructor parameters.</param>
/// <param name="Body">Optional constructor body as an AST node.</param>
/// <param name="BaseCall">Optional base constructor arguments for inheritance chains.</param>
public sealed record ConstructorDefinitionNode(
    IReadOnlyList<Parameter>? Parameters = null,
    Node? Body = null,
    IReadOnlyList<Node>? BaseCall = null,
    AccessModifier AccessModifier = AccessModifier.Public
) : Node {

    public override IEnumerable<Node?> Children {
        get {
            if (Parameters != null)
                foreach (var parameter in Parameters) yield return parameter;
            yield return Body;
            if (BaseCall != null)
                foreach (var arg in BaseCall) yield return arg;
        }
    }

    public override string ToString() {
        var parameterList = Parameters != null ? string.Join(", ", Parameters) : string.Empty;
        return $"ctor({parameterList})";
    }
}