using Poly.Ast.Nodes;
using Poly.Introspection;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// One type-reference → CLR type rule shared by the analysis resolver and the lambda
/// bridge: a CLR or primitive reference wins, then a resolved type reference, then the
/// owner node. A value-type <c>OptionalTypeReference</c> is intentionally not unwrapped
/// (the declared shape is the optional, not its underlying type).
/// </summary>
internal static class ClrTypeResolution {
    public static Type? Resolve(
        Node? typeReference, Node owner, Func<Node, ITypeDefinition?> resolveType) {
        if (typeReference is ClrTypeReference clr)
            return clr.RuntimeType;
        if (typeReference is PrimitiveTypeReference primitive)
            return primitive.PrimitiveId.GetClrType();
        if (typeReference is not null)
            return resolveType(typeReference)?.GetRuntimeType();
        return ReferenceEquals(owner, typeReference)
            ? null
            : resolveType(owner)?.GetRuntimeType();
    }
}