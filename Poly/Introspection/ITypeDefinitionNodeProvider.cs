namespace Poly.Introspection;

using Poly.Ast.Nodes;

/// <summary>A type provider that also exposes the AST type declarations it
/// registered. A compile analyzes those declarations with the root in one
/// pass, so emit never has to analyze a member body itself.</summary>
public interface ITypeDefinitionNodeProvider {
    IEnumerable<TypeDefinitionNode> GetTypeDefinitionNodes();

    /// <summary>The definition this provider built for exactly
    /// <paramref name="node"/>, or null. Node identity, not name: a domain can
    /// hold two definitions with the same name (module and runtime shapes).</summary>
    ITypeDefinition? GetTypeDefinition(TypeDefinitionNode node);
}