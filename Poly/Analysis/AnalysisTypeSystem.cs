namespace Poly.Analysis;

using Poly.Introspection;

/// <summary>
/// The type system one analysis decides by (#998 T12a; docs/decisions/2026-11-19-a-type-system-owns-definitions-
/// vocabulary-and-rules.md §2). It is the analysis' layer: its own sources — the declarations it registers and the
/// provider it was built over — answer a qualified name first, in their registered precedence order, and its
/// <see cref="Target"/> answers everything else. The CLR is reached only as the Target chain's answer, and no provider
/// stack falls back beside it. Not yet through the layer (T12c): C#'s declarations still take the CLR's <c>object</c>,
/// <c>int</c> and <c>void</c> directly. Vocabulary and rules are the Target's until T12c gives the layer its declarations.
/// </summary>
internal sealed class AnalysisTypeSystem(TypeDefinitionProviderCollection providers, ITypeSystem target) : ITypeSystem {
    /// <summary>The analysis' own sources, then the target chain's.</summary>
    public IReadOnlyList<ITypeSource> Sources => [.. providers.Providers.OfType<ITypeSource>(), .. target.Sources];

    public ITypeSystem? Target => target;

    /// <summary>A qualified name through the analysis' own providers, then the Target. A bare name is the unit scope's
    /// when it is registered as an own provider, and the Target's otherwise.</summary>
    public ITypeDefinition? Resolve(string qualifiedName) {
        foreach (var provider in providers.Providers) {
            if (provider.GetTypeDefinition(qualifiedName) is { } definition)
                return definition;
        }
        return target.Resolve(qualifiedName);
    }

    public ITypeDefinition? Bind(string word) => target.Bind(word);
    public string? Spell(ITypeDefinition definition) => target.Spell(definition);
    public bool IsSame(ITypeDefinition a, ITypeDefinition b) => target.IsSame(a, b);
    public ITypeDefinition? NullableOf(ITypeDefinition definition) => target.NullableOf(definition);
    public ITypeDefinition? ElementType(ITypeDefinition definition) => target.ElementType(definition);

    public bool IsAssignableWithoutConversion(ITypeDefinition destination, ITypeDefinition source) =>
        target.IsAssignableWithoutConversion(destination, source);

    public ITypeConversion? Conversion(ITypeDefinition source, ITypeDefinition destination) =>
        target.Conversion(source, destination);

    public ITypeOperator? Operator(OperatorKind kind, ITypeDefinition left, ITypeDefinition? right) =>
        target.Operator(kind, left, right);

    public TRule? Rule<TRule>() where TRule : class, ITypeSystemRule => target.Rule<TRule>();
}