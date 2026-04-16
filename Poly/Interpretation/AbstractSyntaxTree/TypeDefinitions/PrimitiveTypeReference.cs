namespace Poly.Interpretation.AbstractSyntaxTree.TypeDefinitions;

/// <summary>
/// AST node representing a reference to a primitive type by its ID.
/// This allows type definitions to reference primitives without CLR type dependencies.
/// </summary>
/// <param name="PrimitiveId">The primitive type identifier.</param>
/// <param name="IsNullable">Whether this is a nullable version of the primitive type.</param>
public sealed record PrimitiveTypeReference(
    PrimitiveType PrimitiveId,
    bool IsNullable = false
) : Node {

    /// <summary>
    /// Gets the type category for this primitive type.
    /// </summary>
    public TypeCategory Category => PrimitiveId.GetCategory();

    public override string ToString() {
        var name = PrimitiveId.ToString();
        return IsNullable ? $"{name}?" : name;
    }
}