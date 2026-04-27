namespace Poly.Syntax.AbstractSyntaxTree;

public sealed record TypeDefinitionReference(ITypeDefinition TypeDefinition) : TypeReference(TypeDefinition.FullName);