namespace Poly.Interpretation.Analysis;

/// <summary>
/// Analyzes abstract syntax tree nodes using a collection of node analyzers.
/// </summary>
/// <param name="typeDefinitions">The provider for type definitions used during analysis.</param>
/// <param name="analyzers">The collection of node analyzers to apply.</param>
public sealed class Analyzer(ITypeDefinitionProvider typeDefinitions, IEnumerable<INodeAnalyzer> analyzers) {
    private readonly List<Action<AnalysisContext>> _actions = [];

    /// <summary>
    /// Adds a custom action to be executed prior to analysis.
    /// </summary>
    /// <param name="action">The action to add.</param>
    /// <returns>The current Analyzer instance.</returns>
    public Analyzer With(Action<AnalysisContext> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Add(action);
        return this;
    }

    /// <summary>
    /// Analyzes the given AST node and produces an analysis result.
    /// </summary>
    /// <param name="root">The root AST node to analyze.</param>
    /// <returns>The result of the analysis.</returns>
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

        return new AnalysisResult(context.Metadata, context.Diagnostics);
    }
}