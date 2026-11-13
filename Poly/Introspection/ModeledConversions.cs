namespace Poly.Introspection;

/// <summary>
/// Poly's modeled-type conversion convention: a one-parameter static member named <c>op_Implicit</c> or <c>op_Explicit</c>
/// in <see cref="ITypeDefinition.Methods"/>, declared on the source or the destination, the same convention
/// <see cref="ModeledOperators"/> uses for <c>op_*</c> operators. A convention of Poly, not a platform fact.
/// </summary>
internal static class ModeledConversions {
    /// <summary>The conversion from <paramref name="source"/> to <paramref name="destination"/> a modeled definition declares, implicit preferred.</summary>
    public static TypeConversion? Find(ITypeDefinition source, ITypeDefinition destination) {
        if (Find(source, destination, "op_Implicit") is { } declaredImplicit)
            return new TypeConversion(ConversionOperatorKind.Implicit, declaredImplicit);
        if (Find(source, destination, "op_Explicit") is { } declaredExplicit)
            return new TypeConversion(ConversionOperatorKind.Explicit, declaredExplicit);
        return null;
    }

    private static ITypeMethod? Find(ITypeDefinition from, ITypeDefinition to, string name) {
        foreach (var candidate in new[] { from, to }) {
            foreach (var method in candidate.Methods) {
                if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                    continue;
                var parameters = method.Parameters.ToArray();
                if (parameters.Length == 1
                    && TypeDefinitionExtensions.IsSameType(method.MemberTypeDefinition, to)
                    && TypeDefinitionExtensions.IsSameType(parameters[0].ParameterTypeDefinition, from))
                    return method;
            }
        }
        return null;
    }
}