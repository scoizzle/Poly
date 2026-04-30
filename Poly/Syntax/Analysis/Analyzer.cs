namespace Poly.Syntax.Analysis;

/// <summary>
/// Analyzes abstract syntax tree nodes using a collection of node analyzers.
/// </summary>
/// <param name="typeDefinitions">The provider for type definitions used during analysis.</param>
/// <param name="analyzers">The collection of node analyzers to apply.</param>
public sealed class Analyzer(ITypeDefinitionProvider typeDefinitions, IEnumerable<INodeAnalyzer> analyzers) {
    private readonly INodeAnalyzer[] analyzers = analyzers.ToArray();
    private readonly List<Action<AnalysisContext>> _actions = [];

    public ITypeDefinitionProvider TypeDefinitions => typeDefinitions;

    /// <summary>
    /// Adds a custom action to be executed prior to analysis.
    /// </summary>
    /// <param name="action">The action to add.</param>
    /// <returns>The current Analyzer instance.</returns>
    public Analyzer With(Action<AnalysisContext> action) {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Add(action);
        return this;
    }

    /// <summary>
    /// Adds a custom node analyzer to the collection of analyzers.
    /// </summary>
    /// <param name="root">The root AST node to analyze.</param>
    /// <param name="context">The analysis context.</param>
    /// <returns>The result of the analysis.</returns>
    private AnalysisResult AnalyzeInternal(Node root, AnalysisContext context) {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var action in _actions) {
            action(context);
        }

        foreach (var analyzer in analyzers) {
            analyzer.Analyze(context, root);
        }

        return new AnalysisResult(context);
    }

    /// <summary>
    /// Analyzes the given AST node and produces an analysis result.
    /// </summary>
    /// <param name="root">The root AST node to analyze.</param>
    /// <returns>The result of the analysis.</returns>
    public AnalysisResult Analyze(Node root) {
        ArgumentNullException.ThrowIfNull(root);

        var context = new AnalysisContext(typeDefinitions);
        return AnalyzeInternal(root, context);
    }

    /// <summary>
    /// Analyzes the given AST node with reference to a prior analysis result, allowing for incremental analysis.
    /// </summary>
    /// <param name="root">The root AST node to analyze.</param>
    /// <param name="priorAnalysis">The prior analysis result to reference.</param>
    /// <param name="invalidatedNodes">The nodes that have been invalidated and need reanalysis.</param>
    /// <returns>The result of the analysis.</returns>
    public AnalysisResult Analyze(Node root, AnalysisResult priorAnalysis, IEnumerable<Node> invalidatedNodes) {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(priorAnalysis);
        ArgumentNullException.ThrowIfNull(invalidatedNodes);

        if (!priorAnalysis.IsIncrementalAnalysisAvailable()) {
            return Analyze(root);
        }

        var context = new AnalysisContext(
            typeDefinitions,
            priorAnalysis);

        context.SetInvalidatedNodesForIncrementalAnalysis(invalidatedNodes);

        return AnalyzeInternal(root, context);
    }
}