namespace Poly.Syntax.Analysis;

/// <summary>
/// Analyzes abstract syntax tree nodes using a collection of node analyzers.
/// </summary>
/// <param name="typeDefinitions">The provider for type definitions used during analysis.</param>
/// <param name="analyzers">The named analyzers to apply in order.</param>
public sealed class Analyzer(ITypeDefinitionProvider typeDefinitions, IEnumerable<(INodeAnalyzer Analyzer, string PassName)> analyzers) {
    private readonly (INodeAnalyzer Analyzer, string PassName)[] _analyzers = analyzers.ToArray();
    private readonly List<Action<AnalysisContext>> _actions = [];

    /// <summary>
    /// Options that control analysis behavior (including early exit).
    /// </summary>
    public AnalysisOptions Options { get; init; } = AnalysisOptions.Default;

    public ITypeDefinitionProvider TypeDefinitions => typeDefinitions;

    /// <summary>
    /// Adds a custom action to be executed prior to analysis.
    /// </summary>
    public Analyzer With(Action<AnalysisContext> action) {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Add(action);
        return this;
    }

    private AnalysisResult RunPasses(AnalysisContext context, Node root, bool incremental, int invalidatedNodeCount) {
        foreach (var action in _actions) {
            action(context);
        }

        var collector = new AnalysisTelemetryCollector();
        var totalStart = Stopwatch.GetTimestamp();

        foreach (var (analyzer, passName) in _analyzers) {
            if (!context.ShouldContinue(Options))
                break;

            var passStart = Stopwatch.GetTimestamp();
            analyzer.Analyze(context, root);
            collector.RecordPass(passName, Stopwatch.GetElapsedTime(passStart));

            // Re-check after the pass in case it reported a structural failure.
            if (!context.ShouldContinue(Options))
                break;
        }

        var telemetry = collector.ToSnapshot(Stopwatch.GetElapsedTime(totalStart), incremental, invalidatedNodeCount);
        return new AnalysisResult(context, telemetry, Options);
    }

    /// <summary>
    /// Analyzes the given AST node and produces an analysis result with per-pass telemetry.
    /// </summary>
    public AnalysisResult Analyze(Node root) {
        ArgumentNullException.ThrowIfNull(root);
        var context = new AnalysisContext(typeDefinitions);
        return RunPasses(context, root, incremental: false, invalidatedNodeCount: 0);
    }

    /// <summary>
    /// Analyzes the given AST node incrementally and produces an analysis result with per-pass telemetry.
    /// </summary>
    public AnalysisResult Analyze(Node root, AnalysisResult priorAnalysis, IEnumerable<Node> invalidatedNodes) {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(priorAnalysis);
        ArgumentNullException.ThrowIfNull(invalidatedNodes);

        if (!priorAnalysis.IsIncrementalAnalysisAvailable()) {
            return Analyze(root);
        }

        var invalidated = invalidatedNodes.ToArray();
        var context = new AnalysisContext(typeDefinitions, priorAnalysis);
        context.SetInvalidatedNodesForIncrementalAnalysis(invalidated);
        return RunPasses(context, root, incremental: true, invalidatedNodeCount: invalidated.Length);
    }
}