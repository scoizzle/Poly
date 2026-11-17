namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Poly's modeled-type conversion convention: a one-parameter static method marked
/// <see cref="MethodDefinitionNode.Conversion"/>, declared on the source or the destination, implicit preferred. The
/// marker is the whole convention: a method's name never makes it a conversion.
/// </summary>
internal static class ModeledConversions {
    /// <summary>The conversion from <paramref name="source"/> to <paramref name="destination"/> a modeled definition declares, implicit preferred.</summary>
    public static ITypeConversion? Find(ITypeDefinition source, ITypeDefinition destination) {
        if (Find(source, destination, ConversionOperatorKind.Implicit) is { } declaredImplicit)
            return declaredImplicit;
        return Find(source, destination, ConversionOperatorKind.Explicit);
    }

    private static ITypeConversion? Find(ITypeDefinition from, ITypeDefinition to, ConversionOperatorKind kind) {
        foreach (var candidate in new[] { from, to }) {
            foreach (var conversion in candidate.Conversions) {
                if (conversion.Kind != kind || conversion.Method is null)
                    continue;
                if (TypeDefinitionExtensions.IsSameType(conversion.SourceTypeDefinition, from)
                    && TypeDefinitionExtensions.IsSameType(conversion.ResultTypeDefinition, to))
                    return conversion;
            }
        }
        return null;
    }
}