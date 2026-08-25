namespace Poly.Analysis;

public sealed record AnalysisResult : INodeMetadataProvider {
    private readonly NodeMetadataStore _metadata;
    private readonly Dictionary<NodeId, List<Diagnostic>> _diagnostics;
    private readonly Lazy<IReadOnlyList<Diagnostic>> _allDiagnostics;

    public AnalysisResult(AnalysisContext context, AnalysisTelemetry telemetry, AnalysisOptions? options = null) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(telemetry);
        _metadata = context.Metadata;
        _diagnostics = context.Diagnostics;
        var diagnosticConfiguration = context.Settings.Get<AnalysisDiagnosticConfiguration>()
            ?? AnalysisDiagnosticConfiguration.Default;
        _allDiagnostics = new Lazy<IReadOnlyList<Diagnostic>>(() =>
            _diagnostics
                .SelectMany(kvp => kvp.Value)
                .Select(d => d with {
                    Severity = diagnosticConfiguration.NormalizeSeverity(d.Severity)
                })
                .Where(d => diagnosticConfiguration.ShouldInclude(d.Severity))
                .DistinctBy(d => (d.Node.Id, d.Severity, d.Code, d.Message))
                .ToList());
        Telemetry = telemetry;
        HasStructuralFailure = context.HasStructuralFailure;
        var effectiveOptions = options ?? AnalysisOptions.Default;
        AnalysisWasTerminatedEarly = !context.ShouldContinue(effectiveOptions);
        SettingsUsed = context.Settings;
    }

    /// <summary>
    /// The options that were active during this analysis run.
    /// </summary>
    public AnalysisOptions OptionsUsed { get; } = AnalysisOptions.Default;

    /// <summary>
    /// Settings that were active on the analysis context for this run.
    /// </summary>
    public AnalysisSettings SettingsUsed { get; } = AnalysisSettings.Default;

    /// <summary>
    /// Gets the per-pass timing telemetry captured during analysis.
    /// </summary>
    public AnalysisTelemetry Telemetry { get; }

    /// <summary>
    /// Gets the collection of diagnostics produced during analysis.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _allDiagnostics.Value;

    /// <summary>
    /// Returns true if any error-level diagnostics were produced.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Returns true if any structural or reference-level failures were detected during analysis.
    /// When true, many semantic and higher-level analyses may be incomplete or invalid.
    /// This is the primary signal for early termination and invalidation in incremental scenarios.
    /// </summary>
    public bool HasStructuralFailure { get; init; }

    /// <summary>
    /// Returns true if analysis was terminated early due to errors (structural or otherwise).
    /// The result may be incomplete.
    /// </summary>
    public bool AnalysisWasTerminatedEarly { get; init; }

    /// <summary>
    /// Gets metadata of the specified type for the given node.
    /// Returns null if no metadata of that type exists for the node.
    /// </summary>
    /// <typeparam name="TMetadata">The type of metadata to retrieve.</typeparam>
    /// <param name="node">The node for which to retrieve metadata.</param>
    /// <returns>The metadata of the specified type, or null if not found.</returns>
    public TMetadata? GetMetadata<TMetadata>(Node? node) where TMetadata : class, IAnalysisMetadata => _metadata.Get<TMetadata>(node);

    internal NodeMetadataStore GetMetadataStore() => _metadata;
    internal Dictionary<NodeId, List<Diagnostic>> GetDiagnosticsDictionary() => _diagnostics;
}