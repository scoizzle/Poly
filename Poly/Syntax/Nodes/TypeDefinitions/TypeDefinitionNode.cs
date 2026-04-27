namespace Poly.Syntax.Nodes;

/// <summary>
/// AST node representing a type definition with its members.
/// This is a structural representation - semantic meaning (ITypeDefinition)
/// is extracted by analysis passes.
/// </summary>
/// <param name="Name">The simple name of the type.</param>
/// <param name="Namespace">Optional namespace qualification.</param>
/// <param name="Constructors">Constructor definitions for the type.</param>
/// <param name="Properties">Property definitions for the type.</param>
/// <param name="Methods">Method definitions for the type.</param>
/// <param name="Fields">Field definitions for the type.</param>
/// <param name="BaseType">Optional base type reference.</param>
/// <param name="Interfaces">Interface types this type implements.</param>
/// <param name="GenericParameters">Generic type parameters for generic types.</param>
/// <param name="PrimitiveTypeId">Primitive type identifier if this is a primitive type.</param>
/// <param name="TypeCategory">Category flags describing the type's nature.</param>
public sealed record TypeDefinitionNode(
    string Name,
    string? Namespace = null,
    IReadOnlyList<ConstructorDefinitionNode>? Constructors = null,
    IReadOnlyList<PropertyDefinitionNode>? Properties = null,
    IReadOnlyList<MethodDefinitionNode>? Methods = null,
    IReadOnlyList<FieldDefinitionNode>? Fields = null,
    Node? BaseType = null,
    IReadOnlyList<Node>? Interfaces = null,
    IReadOnlyList<Parameter>? GenericParameters = null,
    PrimitiveType? PrimitiveTypeId = null,
    TypeCategory TypeCategory = TypeCategory.None
) : Node {

    /// <summary>
    /// Gets the fully qualified name combining Namespace and Name.
    /// </summary>
    public string FullName => Namespace != null ? $"{Namespace}.{Name}" : Name;

    /// <summary>
    /// Gets all member definition nodes.
    /// </summary>
    public IEnumerable<Node> Members => [.. Constructors ?? [], .. Properties ?? [], .. Methods ?? [], .. Fields ?? []];

    public override IEnumerable<Node?> Children {
        get {
            if (Constructors != null)
                foreach (var constructor in Constructors) yield return constructor;
            if (Properties != null)
                foreach (var p in Properties) yield return p;
            if (Methods != null)
                foreach (var m in Methods) yield return m;
            if (Fields != null)
                foreach (var f in Fields) yield return f;
            yield return BaseType;
            if (Interfaces != null)
                foreach (var i in Interfaces) yield return i;
            if (GenericParameters != null)
                foreach (var g in GenericParameters) yield return g;
        }
    }

    public override string ToString() => FullName;
}