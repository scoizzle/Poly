namespace Poly.Ast.Nodes;

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
    bool IsAsync = false,
    bool IsOverride = false,
    IReadOnlyList<Parameter>? GenericParameters = null,
    AccessModifier AccessModifier = AccessModifier.Public
) : MemberDefinitionNode(Name, ReturnType, IsStatic, AccessModifier) {

    /// <summary>
    /// Attributes applied to this method.
    /// </summary>
    public IReadOnlyList<AttributeNode> Attributes { get; init; } = [];

    public override IEnumerable<Node?> Children {
        get {
            yield return ReturnType;
            foreach (var a in Attributes) yield return a;
            if (Parameters != null)
                foreach (var p in Parameters) yield return p;
            yield return Body;
            if (GenericParameters != null)
                foreach (var g in GenericParameters) yield return g;
        }
    }

    public override string ToString() {
        var staticPrefix = IsStatic ? "static " : "";
        var asyncPrefix = IsAsync ? "async " : "";
        var overridePrefix = IsOverride ? "override " : "";
        var paramList = Parameters != null ? string.Join(", ", Parameters) : "";
        return $"{asyncPrefix}{staticPrefix}{overridePrefix}{ReturnType} {Name}({paramList})";
    }
}