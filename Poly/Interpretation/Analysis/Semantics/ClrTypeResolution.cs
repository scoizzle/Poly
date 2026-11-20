using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// One type-reference → CLR type rule shared by the analysis resolver and the lambda
/// bridge: a stated definition wins, then a resolved type reference, then the owner
/// node. A value-type <c>OptionalTypeReference</c> is intentionally not unwrapped (the
/// declared shape is the optional, not its underlying type).
/// </summary>
internal static class ClrTypeResolution {
    public static Type? Resolve(
        Node? typeReference, Node owner, Func<Node, ITypeDefinition?> resolveType) {
        var definition = typeReference switch {
            TypeDefinitionReference stated => stated.TypeDefinition,
            not null => resolveType(typeReference),
            null => ReferenceEquals(owner, typeReference) ? null : resolveType(owner)
        };
        return definition?.GetRuntimeType();
    }
}