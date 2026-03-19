namespace Poly.Interpretation.AbstractSyntaxTree.TypeDefinitions;

/// <summary>
/// AST node representing a property definition on a type.
/// </summary>
/// <param name="Name">The property name.</param>
/// <param name="PropertyType">The type of the property.</param>
/// <param name="DefaultValue">Optional default value expression.</param>
/// <param name="IsStatic">Whether this is a static property.</param>
/// <param name="IsReadOnly">Whether this property is read-only (has no setter).</param>
/// <param name="IndexParameters">Parameters for indexed properties (indexers).</param>
/// <param name="Constraints">Validation constraints on the property.</param>
public sealed record PropertyDefinitionNode(
    string Name,
    Node PropertyType,
    Node? DefaultValue = null,
    bool IsStatic = false,
    bool IsReadOnly = false,
    IReadOnlyList<Parameter>? IndexParameters = null,
    IReadOnlyList<Node>? Constraints = null
) : MemberDefinitionNode(Name, PropertyType, IsStatic) {

    public override IEnumerable<Node?> Children {
        get {
            yield return PropertyType;
            yield return DefaultValue;
            if (IndexParameters != null)
                foreach (var p in IndexParameters) yield return p;
            if (Constraints != null)
                foreach (var c in Constraints) yield return c;
        }
    }

    public override string ToString() {
        var suffix = DefaultValue != null ? $" = {DefaultValue}" : "";
        var staticPrefix = IsStatic ? "static " : "";
        return $"{staticPrefix}{PropertyType} {Name}{suffix}";
    }
}