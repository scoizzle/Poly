namespace Poly.Ast.Nodes;

public sealed record TypeDefinitionReference(ITypeDefinition TypeDefinition) : TypeReference(TypeDefinition.FullName);