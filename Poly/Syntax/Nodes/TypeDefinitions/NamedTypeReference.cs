namespace Poly.Ast.Nodes;

/// <summary>
/// AST node representing a reference to a named type (by name, not CLR Type).
/// Used for referencing user-defined types, data model types, etc.
/// </summary>
/// <param name="TypeName">The name of the type being referenced.</param>
/// <param name="Namespace">Optional namespace qualification.</param>
/// <param name="TypeArguments">Generic type arguments if this is a closed generic type.</param>
public sealed record NamedTypeReference(
    string TypeName,
    string? Namespace = null,
    IReadOnlyList<Node>? TypeArguments = null
) : Node {

    /// <summary>
    /// Gets the fully qualified name.
    /// </summary>
    public string FullName => Namespace != null ? $"{Namespace}.{TypeName}" : TypeName;

    public override IEnumerable<Node?> Children => TypeArguments ?? [];

    public override string ToString() {
        if (TypeArguments == null || TypeArguments.Count == 0)
            return FullName;
        return $"{FullName}<{string.Join(", ", TypeArguments)}>";
    }
}