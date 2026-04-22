namespace Poly.Interpretation.AbstractSyntaxTree;

public sealed record TypeDefinitionReference(ITypeDefinition TypeDefinition) : TypeReference(TypeDefinition.FullName);