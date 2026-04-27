namespace Poly.Syntax.AbstractSyntaxTree;

/// <summary>
/// AST node representing a field definition on a type.
/// </summary>
/// <param name="Name">The field name.</param>
/// <param name="FieldType">The type of the field.</param>
/// <param name="DefaultValue">Optional default value expression.</param>
/// <param name="IsStatic">Whether this is a static field.</param>
/// <param name="IsReadOnly">Whether this is a readonly field.</param>
public sealed record FieldDefinitionNode(
    string Name,
    Node FieldType,
    Node? DefaultValue = null,
    bool IsStatic = false,
    bool IsReadOnly = false
) : MemberDefinitionNode(Name, FieldType, IsStatic) {

    public override IEnumerable<Node?> Children {
        get {
            yield return FieldType;
            yield return DefaultValue;
        }
    }

    public override string ToString() {
        var suffix = DefaultValue != null ? $" = {DefaultValue}" : "";
        var staticPrefix = IsStatic ? "static " : "";
        var readonlyPrefix = IsReadOnly ? "readonly " : "";
        return $"{staticPrefix}{readonlyPrefix}{FieldType} {Name}{suffix}";
    }
}