using Poly.Interpretation.Analysis;

namespace Poly.Syntax.Analysis;

/// <summary>
/// Fluent builder for constructing <see cref="Analyzer"/> pipelines.
///
/// Automatically topologically sorts registered passes according to the
/// dependency table in <see cref="PassDependencyTable"/>. Passes not declared
/// in the table (e.g. custom or test analyzers) remain in their registration
/// order at the end of the pipeline. Circular dependencies throw at build time.
/// </summary>
public sealed class AnalyzerBuilder {
    private readonly List<(INodeAnalyzer Analyzer, string PassName)> _analyzers = new();

    /// <summary>
    /// Adds an analyzer to the pipeline. The pass name defaults to the analyzer's type name.
    /// </summary>
    public AnalyzerBuilder AddAnalyzer(INodeAnalyzer analyzer) {
        ArgumentNullException.ThrowIfNull(analyzer);
        _analyzers.Add((analyzer, analyzer.GetType().Name));
        return this;
    }

    /// <summary>
    /// Adds an analyzer to the pipeline with an explicit pass name.
    /// </summary>
    public AnalyzerBuilder AddAnalyzer(INodeAnalyzer analyzer, string passName) {
        ArgumentNullException.ThrowIfNull(analyzer);
        _analyzers.Add((analyzer, passName));
        return this;
    }

    /// <summary>
    /// Build the analyzer pipeline. Passes declared in <see cref="PassDependencyTable"/>
    /// are topologically sorted to satisfy their dependencies. Unknown passes are
    /// appended in registration order. Throws on circular dependencies.
    /// </summary>
    public Analyzer Build() {
        TopologicalSort();
        return new Analyzer([.. _analyzers]);
    }

    private void TopologicalSort() {
        var deps = PassDependencyTable.Dependencies;

        var order = new Dictionary<string, long>(StringComparer.Ordinal);

        void Compute(string passName) {
            if (order.TryGetValue(passName, out var existing)) {
                if (existing == long.MaxValue)
                    throw new InvalidOperationException(
                        $"Circular dependency detected involving pass '{passName}'.");
                return;
            }

            order[passName] = long.MaxValue;
            long value = 0;
            if (deps.TryGetValue(passName, out var required)) {
                foreach (var req in required) {
                    Compute(req);
                    if (order[req] > value)
                        value = order[req];
                }
            }
            order[passName] = value + 1;
        }

        foreach (var (_, name) in _analyzers) {
            if (deps.ContainsKey(name))
                Compute(name);
        }
        for (int i = 0; i < _analyzers.Count; i++) {
            if (!deps.ContainsKey(_analyzers[i].PassName))
                order[_analyzers[i].PassName] = i;
        }

        _analyzers.Sort((a, b) => order.GetValueOrDefault(a.PassName, 0L)
            .CompareTo(order.GetValueOrDefault(b.PassName, 0L)));
    }
}