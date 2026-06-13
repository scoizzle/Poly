namespace Poly.Syntax.Analysis;

/// <summary>
/// Fluent builder for constructing <see cref="Analyzer"/> pipelines.
/// </summary>
public sealed class AnalyzerBuilder {
    private readonly List<(INodeAnalyzer Analyzer, string PassName)> _analyzers = new();
    private AnalysisOptions _options = AnalysisOptions.Default;

    /// <summary>
    /// Registers a node analyzer pass in the pipeline.
    /// </summary>
    public AnalyzerBuilder AddAnalyzer(INodeAnalyzer analyzer, string? passName = null) {
        ArgumentNullException.ThrowIfNull(analyzer);
        _analyzers.Add((analyzer, passName ?? analyzer.GetType().Name));
        return this;
    }

    /// <summary>
    /// Sets analysis options for this pipeline.
    /// </summary>
    public AnalyzerBuilder WithOptions(AnalysisOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        return this;
    }

    /// <summary>
    /// Builds an <see cref="Analyzer"/> with the configured passes.
    /// </summary>
    public Analyzer Build() {
        return new Analyzer(_analyzers.ToArray()) { Options = _options };
    }
}