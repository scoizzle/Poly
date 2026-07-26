namespace Poly.Ast.Nodes;

/// <summary>A <see cref="TypeReference"/> variant that directly holds
/// a resolved <see cref="ITypeDefinition"/>, bypassing string-based
/// type name resolution.  Created during analysis passes (e.g., constant
/// folding) to indicate the target type of a <see cref="TypeCast"/>
/// without requiring the type name to be re-resolved.</summary>
public sealed record ResolvedTypeReference(ITypeDefinition TypeDefinition) : Node {
}