namespace Poly.Analysis;

/// <summary>
/// Runs an ordered pipeline of analysis passes over an AST node.
/// Immutable after construction — safe for repeated use (passes are stateless).
/// </summary>
public sealed class Analyzer(
    IReadOnlyList<INodeAnalyzer> analyzers,
    AnalysisOptions? options = null) {
    private readonly IReadOnlyList<INodeAnalyzer> _analyzers = analyzers;

    /// <summary>Registration order from <see cref="AnalyzerBuilder"/>.</summary>
    internal IReadOnlyList<string> PassNames => [.. _analyzers.Select(static a => a.PassName)];

    /// <summary>
    /// Options that control analysis behavior (including early exit).
    /// </summary>
    public AnalysisOptions Options { get; } = options ?? AnalysisOptions.Default;

    private AnalysisResult RunPasses(AnalysisContext context, Node root) {
        var collector = new AnalysisTelemetryCollector();
        var totalStart = Stopwatch.GetTimestamp();

        foreach (var analyzer in _analyzers) {
            if (Options.ShouldStopOnErrors && context.HasErrors)
                break;

            var passStart = Stopwatch.GetTimestamp();
            analyzer.Analyze(context, root);
            collector.RecordPass(analyzer.PassName, Stopwatch.GetElapsedTime(passStart));
        }

        var telemetry = collector.ToSnapshot(Stopwatch.GetElapsedTime(totalStart));
        return new AnalysisResult(
            new NodeMetadataStore(context.Metadata),
            telemetry,
            context.Diagnostics.ToList(),
            context.Settings,
            Options,
            context.HasErrors) {
            TypeSystem = context.TypeSystem
        };
    }

    /// <summary>
    /// Analyzes the given root node in this context's type system. A caller that supplies its own sources registers them
    /// through <c>setup</c> (#998 T12b).
    /// </summary>
    public AnalysisResult Analyze(Node root,
        Action<AnalysisContext>? setup = null,
        AnalysisSettings? settings = null) {

        ArgumentNullException.ThrowIfNull(root);
        settings ??= AnalysisSettings.Default;
        var context = new AnalysisContext(settings);
        setup?.Invoke(context);
        return RunPasses(context, root);
    }
}