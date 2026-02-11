namespace Poly.Interpretation.AbstractSyntaxTree;

public sealed record TypeDefinitionReference(ITypeDefinition TypeDefinition) : Node {
    public override string ToString() => TypeDefinition.FullName;
}