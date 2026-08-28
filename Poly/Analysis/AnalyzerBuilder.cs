namespace Poly.Analysis;

/// <summary>
/// Fluent builder for constructing <see cref="Analyzer"/> pipelines.
///
/// Passes are inserted after their last declared
/// <see cref="INodeAnalyzer.Dependencies"/> name. A declared name that is not
/// registered fails closed. Circular dependencies throw at registration time.
/// <see cref="Analyzer"/> awaits those dependencies; independent passes overlap.
/// </summary>
public sealed class AnalyzerBuilder {
    private readonly OrderedDictionary<string, INodeAnalyzer> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Adds an analyzer. The pass is inserted after its last declared dependency
    /// (or at the end if it has none). Throws if a declared dependency is not
    /// registered in this pipeline.
    /// </summary>
    public AnalyzerBuilder AddAnalyzer(INodeAnalyzer analyzer) {
        ArgumentNullException.ThrowIfNull(analyzer);

        var offset = analyzer.Dependencies.Length == 0
            ? _entries.Count
            : analyzer.Dependencies.Max(e => _entries.IndexOf(e) switch {
                -1 => throw new InvalidOperationException(
                    $"Pass '{analyzer.PassName}' depends on '{e}' which is not registered in this pipeline."),
                int.MaxValue => throw new InvalidOperationException(
                    $"Pass '{analyzer.PassName}' depends on '{e}' but a circular dependency was detected."),
                var i => i + 1
            });

        _entries.Insert(offset, analyzer.PassName, analyzer);
        return this;
    }

    /// <summary>
    /// Build the analyzer pipeline.
    /// </summary>
    public Analyzer Build() {
        return new Analyzer([.. _entries.Values]);
    }
}