namespace Poly.Syntax.Nodes;

public sealed record TypeDefinitionReference(ITypeDefinition TypeDefinition) : TypeReference(TypeDefinition.FullName);