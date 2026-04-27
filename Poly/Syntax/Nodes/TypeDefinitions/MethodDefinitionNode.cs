namespace Poly.Syntax.Nodes;

/// <summary>
/// AST node representing a method definition on a type.
/// </summary>
/// <param name="Name">The method name.</param>
/// <param name="ReturnType">The return type of the method.</param>
/// <param name="Parameters">The method parameters.</param>
/// <param name="Body">Optional method body as an AST node.</param>
/// <param name="IsStatic">Whether this is a static method.</param>
/// <param name="GenericParameters">Generic type parameters for generic methods.</param>
public sealed record MethodDefinitionNode(
    string Name,
    Node ReturnType,
    IReadOnlyList<Parameter>? Parameters = null,
    Node? Body = null,
    bool IsStatic = false,
    IReadOnlyList<Parameter>? GenericParameters = null
) : MemberDefinitionNode(Name, ReturnType, IsStatic) {

    public override IEnumerable<Node?> Children {
        get {
            yield return ReturnType;
            if (Parameters != null)
                foreach (var p in Parameters) yield return p;
            yield return Body;
            if (GenericParameters != null)
                foreach (var g in GenericParameters) yield return g;
        }
    }

    public override string ToString() {
        var staticPrefix = IsStatic ? "static " : "";
        var paramList = Parameters != null ? string.Join(", ", Parameters) : "";
        return $"{staticPrefix}{ReturnType} {Name}({paramList})";
    }
}