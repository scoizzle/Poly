namespace Poly.Analysis;

using Poly.Ast.Nodes;

/// <summary>A source of an analysis' declarations that also exposes the AST type declarations it registered. A compile
/// analyzes those declarations with the root in one pass, so emit never has to analyze a member body itself. An
/// analysis' own question about its tree, so it lives with the analysis, not in Introspection (#998 T13).</summary>
internal interface ITypeDefinitionNodeProvider {
    IEnumerable<TypeDefinitionNode> GetTypeDefinitionNodes();

    /// <summary>The definition this provider built for exactly
    /// <paramref name="node"/>, or null. Node identity, not name: a domain can
    /// hold two definitions with the same name (module and runtime shapes).</summary>
    ITypeDefinition? GetTypeDefinition(TypeDefinitionNode node);
}