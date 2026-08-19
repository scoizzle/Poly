using System.Collections.Concurrent;

namespace Poly.Analysis;

/// <summary>
/// Runs an ordered pipeline of analysis passes over an AST node.
/// Immutable after construction — safe for repeated use (passes are stateless).
/// </summary>
public sealed class Analyzer {
    private readonly INodeAnalyzer[] _analyzers;

    internal Analyzer(INodeAnalyzer[] analyzers) {
        _analyzers = analyzers;
    }

    /// <summary>
    /// Options that control analysis behavior (including early exit).
    /// </summary>
    public AnalysisOptions Options { get; init; } = AnalysisOptions.Default;

    private AnalysisResult RunPasses(AnalysisContext context, Node root, bool incremental, int invalidatedNodeCount) {
        // Each pass runs in its own task, but the tasks are chained by dependencies so that
        // a pass only runs after all of its dependencies have completed.  This allows
        // independent passes to run in parallel while still respecting the dependency order.
        // This assumes _analyzers is a DAG (no cycles) and that all dependencies are present 
        // in the list, and in the correct order. AnalyzerBuilder enforces this at construction time.

        var collector = new AnalysisTelemetryCollector();
        var totalStart = Stopwatch.GetTimestamp();
        var passes = new ConcurrentDictionary<string, Task>();

        foreach (var analyzer in _analyzers) {
            passes[analyzer.PassName] = RunPassAsync(analyzer);
        }

        Task.WaitAll(passes.Values);
        var telemetry = collector.ToSnapshot(Stopwatch.GetElapsedTime(totalStart), incremental, invalidatedNodeCount);
        return new AnalysisResult(context, telemetry, Options);

        async Task RunPassAsync(INodeAnalyzer analyzer) {
            var dependencies = analyzer.Dependencies.Select(e => passes[e]);
            await Task.WhenAll(dependencies).ConfigureAwait(false);

            if (!context.ShouldContinue(Options))
                return;

            var passStart = Stopwatch.GetTimestamp();
            analyzer.Analyze(context, root);
            collector.RecordPass(analyzer.PassName, Stopwatch.GetElapsedTime(passStart));
        }
    }

    /// <summary>
    /// Analyzes the given root node.
    /// </summary>
    public AnalysisResult Analyze(Node root,
        ITypeDefinitionProvider? typeDefinitions = null,
        Action<AnalysisContext>? setup = null,
        AnalysisSettings? settings = null) {

        ArgumentNullException.ThrowIfNull(root);
        var context = new AnalysisContext(typeDefinitions ?? Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared, settings);
        setup?.Invoke(context);
        return RunPasses(context, root, incremental: false, invalidatedNodeCount: 0);
    }

    /// <summary>
    /// Analyzes the given root node incrementally.
    /// </summary>
    public AnalysisResult Analyze(Node root,
        AnalysisResult priorAnalysis, IEnumerable<Node> invalidatedNodes,
        ITypeDefinitionProvider? typeDefinitions = null,
        Action<AnalysisContext>? setup = null,
        AnalysisSettings? settings = null) {

        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(priorAnalysis);
        ArgumentNullException.ThrowIfNull(invalidatedNodes);

        if (!priorAnalysis.IsIncrementalAnalysisAvailable() || invalidatedNodes.Contains(root)) {
            return Analyze(root, typeDefinitions, setup, settings);
        }

        var invalidated = invalidatedNodes.ToArray();
        var context = new AnalysisContext(typeDefinitions ?? Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared, priorAnalysis, settings);
        context.SetInvalidatedNodesForIncrementalAnalysis(invalidated);
        setup?.Invoke(context);
        return RunPasses(context, root, incremental: true, invalidatedNodeCount: invalidated.Length);
    }
}