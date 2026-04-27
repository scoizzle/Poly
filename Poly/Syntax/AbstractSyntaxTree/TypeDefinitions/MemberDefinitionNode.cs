namespace Poly.Syntax.AbstractSyntaxTree;

/// <summary>
/// Base AST node for type member definitions (properties, methods, fields).
/// </summary>
/// <param name="Name">The name of the member.</param>
/// <param name="MemberType">The type of the member (property type, return type, field type).</param>
/// <param name="IsStatic">Whether this is a static member.</param>
public abstract record MemberDefinitionNode(
    string Name,
    Node MemberType,
    bool IsStatic = false
) : Node {

    public override IEnumerable<Node?> Children => [MemberType];

    public override string ToString() => $"{MemberType} {Name}";
}