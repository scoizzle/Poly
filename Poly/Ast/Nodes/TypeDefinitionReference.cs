namespace Poly.Ast.Nodes;

/// <summary>A type named by its resolved definition, as the analysis states it. Prints as the type's name, like every
/// other reference, so a printed tree is deterministic whatever the node's identity.</summary>
public sealed record TypeDefinitionReference(ITypeDefinition TypeDefinition) : TypeReference(TypeDefinition.FullName) {
    public override string ToString() => TypeName;
}