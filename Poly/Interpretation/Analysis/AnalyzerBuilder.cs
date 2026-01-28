using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.Analysis;

public sealed class AnalyzerBuilder {
    private readonly TypeDefinitionProviderCollection _typeDefinitions = [ClrTypeDefinitionRegistry.Shared];
    private readonly List<INodeAnalyzer> _analyzers = new();

    public void AddAnalyzer(INodeAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        _analyzers.Add(analyzer);
    }

    public void AddTypeDefinitionProvider(ITypeDefinitionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _typeDefinitions.Add(provider);
    }

    public Analyzer Build()
    {
        TypeDefinitionProviderCollection typeDefinitionProviders = [.. _typeDefinitions.Providers];
        return new Analyzer(typeDefinitionProviders, _analyzers.ToArray());
    }
}