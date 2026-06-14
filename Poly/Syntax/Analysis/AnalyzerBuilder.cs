namespace Poly.Syntax.Analysis;

/// <summary>
/// Fluent builder for constructing <see cref="Analyzer"/> pipelines.
/// </summary>
public sealed class AnalyzerBuilder {
    private readonly List<(INodeAnalyzer Analyzer, string PassName)> _analyzers = new();

    public AnalyzerBuilder AddAnalyzer(INodeAnalyzer analyzer, string? passName = null) {
        ArgumentNullException.ThrowIfNull(analyzer);
        _analyzers.Add((analyzer, passName ?? analyzer.GetType().Name));
        return this;
    }

    public Analyzer Build() {
        return new Analyzer(_analyzers.ToArray());
    }
}