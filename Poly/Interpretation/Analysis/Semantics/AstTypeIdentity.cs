namespace Poly.Interpretation.Analysis.Semantics;

using Poly.Introspection;

/// <summary>
/// The identity of the definitions C# spells from an AST: a declared type is the same only as itself; two synthetic
/// collections of one kind and element are one type (each resolve builds a fresh instance, and a nested collection
/// compares by this notion again, since its name does not carry its element's namespace); two generic parameters are one
/// when they share a name and a declaring type. C#'s system and an analysis layer share this body (#998 T9, T12c), so
/// neither owns a divergent copy.
/// </summary>
internal static class AstTypeIdentity {
    public static bool IsSame(ITypeDefinition a, ITypeDefinition b) {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (ReferenceEquals(a, b))
            return true;
        return (a, b) switch {
            (AstCollectionTypeDefinition left, AstCollectionTypeDefinition right) when left.Kind == right.Kind =>
                left.ElementType is AstCollectionTypeDefinition || right.ElementType is AstCollectionTypeDefinition
                    ? TypeDefinitionExtensions.IsSameType(left.ElementType, right.ElementType)
                    : string.Equals(left.ElementType.Name, right.ElementType.Name, StringComparison.Ordinal)
                      && string.Equals(left.ElementType.Namespace, right.ElementType.Namespace, StringComparison.Ordinal),
            (AstGenericParameterTypeDefinition left, AstGenericParameterTypeDefinition right) =>
                string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                && TypeDefinitionExtensions.IsSameType(left.DeclaringType, right.DeclaringType),
            _ => false,
        };
    }
}