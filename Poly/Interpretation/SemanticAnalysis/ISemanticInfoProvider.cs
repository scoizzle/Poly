namespace Poly.Interpretation.SemanticAnalysis;

/// <summary>
/// Provides semantic information about AST nodes without exposing implementation details.
/// Implementations manage caching and resolution of type information.
/// </summary>
public interface ISemanticInfoProvider
{
    /// <summary>
    /// Gets the resolved type definition for a node.
    /// </summary>
    ITypeDefinition? GetResolvedType(Node node);

    /// <summary>
    /// Gets the resolved member for a node (for member access expressions).
    /// </summary>
    ITypeMember? GetResolvedMember(Node node);

    /// <summary>
    /// Determines whether the given node has any semantic information.
    /// </summary>
    bool HasSemanticInfo(Node node);
}
