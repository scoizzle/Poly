using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Syntax.Analysis;

public sealed class AnalyzerBuilder {
    private readonly TypeDefinitionProviderCollection _typeDefinitions;
    private readonly List<(INodeAnalyzer Analyzer, string PassName)> _analyzers = new();

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

    public AnalyzerBuilder AddAnalyzer(INodeAnalyzer analyzer, string? passName = null) {
        ArgumentNullException.ThrowIfNull(analyzer);
        _analyzers.Add((analyzer, passName ?? analyzer.GetType().Name));
        return this;
    }

    public AnalyzerBuilder AddTypeDefinitionProvider(ITypeDefinitionProvider provider) {
        ArgumentNullException.ThrowIfNull(provider);
        _typeDefinitions.Add(provider);
        return this;
    }

    private AnalysisOptions _options = AnalysisOptions.Default;

    public AnalyzerBuilder WithOptions(AnalysisOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        return this;
    }

    public Analyzer Build() {
        TypeDefinitionProviderCollection typeDefinitionProviders = [.. _typeDefinitions.Providers];
        return new Analyzer(typeDefinitionProviders, _analyzers.ToArray()) { Options = _options };
    }
}