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

        foreach (var analyzer in _analyzers) {
            if (!context.ShouldContinue(Options))
                break;

            var passStart = Stopwatch.GetTimestamp();
            analyzer.Analyze(context, root);
            collector.RecordPass(analyzer.PassName, Stopwatch.GetElapsedTime(passStart));
        }

        var telemetry = collector.ToSnapshot(Stopwatch.GetElapsedTime(totalStart));
        // Preserve prior contract: same Node/Severity/Code/Message reports once.
        var diagnostics = context.Diagnostics
            .DistinctBy(d => (d.Node.Id, d.Severity, d.Code, d.Message))
            .ToList();
        return new AnalysisResult(
            new NodeMetadataStore(context.Metadata),
            telemetry,
            diagnostics,
            context.Settings,
            Options);
    }

    /// <summary>
    /// Analyzes the given root node.
    /// </summary>
    public AnalysisResult Analyze(Node root,
        ITypeDefinitionProvider? typeDefinitions = null,
        Action<AnalysisContext>? setup = null,
        AnalysisSettings? settings = null) {

        ArgumentNullException.ThrowIfNull(root);
        typeDefinitions ??= Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared;
        settings ??= AnalysisSettings.Default;
        var context = new AnalysisContext(typeDefinitions, settings);
        setup?.Invoke(context);
        return RunPasses(context, root);
    }
}