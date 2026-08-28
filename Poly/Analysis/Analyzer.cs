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

    /// <summary>Registration order after <see cref="AnalyzerBuilder"/> topological insert.</summary>
    internal IReadOnlyList<string> PassNames => [.. _analyzers.Select(static a => a.PassName)];

    /// <summary>
    /// Options that control analysis behavior (including early exit).
    /// </summary>
    public AnalysisOptions Options { get; init; } = AnalysisOptions.Default;

    private AnalysisResult RunPasses(AnalysisContext context, Node root) {
        var collector = new AnalysisTelemetryCollector();
        var totalStart = Stopwatch.GetTimestamp();
        var passes = new ConcurrentDictionary<string, Task>(StringComparer.Ordinal);

        foreach (var analyzer in _analyzers) {
            passes[analyzer.PassName] = RunPassAsync(analyzer);
        }

        Task.WaitAll(passes.Values);
        var telemetry = collector.ToSnapshot(Stopwatch.GetElapsedTime(totalStart));
        return new AnalysisResult(context, telemetry, Options);

        async Task RunPassAsync(INodeAnalyzer analyzer) {
            await Task.WhenAll(analyzer.Dependencies.Select(name => passes[name])).ConfigureAwait(false);

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
        return RunPasses(context, root);
    }
}