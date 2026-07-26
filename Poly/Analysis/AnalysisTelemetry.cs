namespace Poly.Analysis;

public sealed record AnalyzerPassTelemetry(
    string PassName,
    TimeSpan Elapsed
);

public sealed record AnalysisTelemetry(
    IReadOnlyList<AnalyzerPassTelemetry> Passes,
    TimeSpan TotalElapsed,
    bool Incremental,
    int InvalidatedNodeCount
) {
    public static readonly AnalysisTelemetry Empty = new([], TimeSpan.Zero, Incremental: false, InvalidatedNodeCount: 0);
}

internal sealed class AnalysisTelemetryCollector {
    private readonly List<AnalyzerPassTelemetry> _passes = [];

    public void RecordPass(string passName, TimeSpan elapsed) {
        _passes.Add(new AnalyzerPassTelemetry(passName, elapsed));
    }

    public AnalysisTelemetry ToSnapshot(TimeSpan totalElapsed, bool incremental, int invalidatedNodeCount) {
        return new AnalysisTelemetry(_passes.ToArray(), totalElapsed, incremental, invalidatedNodeCount);
    }
}