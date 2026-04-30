using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Syntax.Analysis;

public sealed class AnalyzerBuilder {
    private readonly TypeDefinitionProviderCollection _typeDefinitions;
    private readonly List<INodeAnalyzer> _analyzers = new();

    public AnalyzerBuilder()
        : this([ClrTypeDefinitionRegistry.Shared]) {
    }

    public AnalyzerBuilder(ITypeDefinitionProvider typeDefinitionProvider)
        : this([typeDefinitionProvider]) {
    }

    public AnalyzerBuilder(IEnumerable<ITypeDefinitionProvider> typeDefinitionProviders) {
        ArgumentNullException.ThrowIfNull(typeDefinitionProviders);
        _typeDefinitions = [.. typeDefinitionProviders];
    }

    public AnalyzerBuilder AddAnalyzer(INodeAnalyzer analyzer) {
        ArgumentNullException.ThrowIfNull(analyzer);
        _analyzers.Add(analyzer);
        return this;
    }

    public AnalyzerBuilder AddTypeDefinitionProvider(ITypeDefinitionProvider provider) {
        ArgumentNullException.ThrowIfNull(provider);
        _typeDefinitions.Add(provider);
        return this;
    }

    public Analyzer Build() {
        TypeDefinitionProviderCollection typeDefinitionProviders = [.. _typeDefinitions.Providers];
        return new Analyzer(typeDefinitionProviders, _analyzers.ToArray());
    }
}