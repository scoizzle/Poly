namespace Poly.Analysis;

/// <summary>
/// Fluent builder for constructing <see cref="Analyzer"/> pipelines.
///
/// <see cref="AddAnalyzer"/> appends. <see cref="Build"/> is that list in
/// registration order — the pipeline file is the schedule.
/// </summary>
public sealed class AnalyzerBuilder {
    private readonly OrderedDictionary<string, INodeAnalyzer> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Appends <paramref name="analyzer"/> in registration order. Duplicate
    /// <see cref="INodeAnalyzer.PassName"/> fails closed.
    /// </summary>
    public AnalyzerBuilder AddAnalyzer(INodeAnalyzer analyzer) {
        ArgumentNullException.ThrowIfNull(analyzer);
        if (string.IsNullOrWhiteSpace(analyzer.PassName))
            throw new ArgumentException("Analyzer PassName must be non-empty.", nameof(analyzer));
        if (!_entries.TryAdd(analyzer.PassName, analyzer))
            throw new InvalidOperationException(
                $"An analyzer with pass name '{analyzer.PassName}' is already registered.");
        return this;
    }

    /// <summary>
    /// Build the analyzer pipeline (registration order).
    /// </summary>
    public Analyzer Build(AnalysisOptions? options = null) {
        return new Analyzer([.. _entries.Values], options);
    }
}