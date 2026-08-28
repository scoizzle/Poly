using System.Collections.Concurrent;

namespace Poly.Analysis;

public sealed record AnalyzerPassTelemetry(
    string PassName,
    TimeSpan Elapsed
);

public sealed record AnalysisTelemetry(
    IReadOnlyList<AnalyzerPassTelemetry> Passes,
    TimeSpan TotalElapsed
) {
    public static readonly AnalysisTelemetry Empty = new([], TimeSpan.Zero);
}

internal sealed class AnalysisTelemetryCollector {
    private readonly ConcurrentQueue<AnalyzerPassTelemetry> _passes = [];

    public void RecordPass(string passName, TimeSpan elapsed) => _passes.Enqueue(new AnalyzerPassTelemetry(passName, elapsed));

    public AnalysisTelemetry ToSnapshot(TimeSpan totalElapsed) =>
        new([.. _passes], totalElapsed);
}