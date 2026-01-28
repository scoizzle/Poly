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

public sealed class Analyzer(ITypeDefinitionProvider typeDefinitions, IEnumerable<INodeAnalyzer> analyzers) {
    private readonly List<Action<AnalysisContext>> _actions = [];

    public Analyzer With(Action<AnalysisContext> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Add(action);
        return this;
    }

    public AnalysisResult Analyze(Node root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var context = new AnalysisContext(typeDefinitions);

        foreach (var action in _actions) {
            action(context);
        }

        foreach (var analyzer in analyzers) {
            analyzer.Analyze(context, root);
        }

        return new AnalysisResult(context.Metadata);
    }
}
