namespace Poly.Syntax.Analysis;

public sealed record AnalyzerPassTelemetry(
    string PassName,
    int InvocationCount,
    TimeSpan Elapsed
);

public sealed record AnalysisTelemetry(
    IReadOnlyList<AnalyzerPassTelemetry> Passes,
    TimeSpan TotalElapsed,
    bool Incremental,
    int InvalidatedNodeCount
);

public sealed record AnalysisRun(
    AnalysisResult Analysis,
    AnalysisTelemetry Telemetry
);

internal sealed class AnalysisTelemetryCollector {
    private readonly Dictionary<string, (int count, TimeSpan elapsed)> _passMetrics = new(StringComparer.Ordinal);

    public void RecordPass(string passName, TimeSpan elapsed) {
        if (_passMetrics.TryGetValue(passName, out var value)) {
            _passMetrics[passName] = (value.count + 1, value.elapsed + elapsed);
            return;
        }

        _passMetrics[passName] = (1, elapsed);
    }

    public AnalysisTelemetry ToSnapshot(TimeSpan totalElapsed, bool incremental, int invalidatedNodeCount) {
        var passes = _passMetrics
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => new AnalyzerPassTelemetry(kvp.Key, kvp.Value.count, kvp.Value.elapsed))
            .ToArray();

        return new AnalysisTelemetry(passes, totalElapsed, incremental, invalidatedNodeCount);
    }
}

internal sealed class TelemetryNodeAnalyzer(INodeAnalyzer inner, string passName, AnalysisTelemetryCollector collector) : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        var start = Stopwatch.GetTimestamp();
        inner.Analyze(context, node);
        var elapsed = Stopwatch.GetElapsedTime(start);
        collector.RecordPass(passName, elapsed);
    }
}