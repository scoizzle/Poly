using Poly.Syntax.Analysis;

namespace Poly.Data.Modeling;

public sealed record DomainOperabilitySnapshot(
    AnalysisRun AnalysisRun,
    NodeInvalidityReport Invalidity
);

public sealed record DomainOperabilityDelta(
    AnalysisRun AnalysisRun,
    NodeInvalidityReport Invalidity,
    DomainDiffReport Diff
);

public static class DomainOperabilityFacade {
    public static DomainOperabilitySnapshot Capture(Domain domain, DomainModelAnalyzer? analyzer = null) {
        ArgumentNullException.ThrowIfNull(domain);

        var subjectAnalyzer = analyzer ?? new DomainModelAnalyzer();
        var analysisRun = subjectAnalyzer.AnalyzeWithTelemetry(domain);
        var invalidity = DomainInvalidityExplainer.Explain(analysisRun.Analysis);

        return new DomainOperabilitySnapshot(analysisRun, invalidity);
    }

    public static DomainOperabilityDelta AnalyzeExplainDiff(Domain before, Domain after, DomainModelAnalyzer? analyzer = null) {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var subjectAnalyzer = analyzer ?? new DomainModelAnalyzer();
        var analysisRun = subjectAnalyzer.AnalyzeWithTelemetry(after);
        var invalidity = DomainInvalidityExplainer.Explain(analysisRun.Analysis);
        var diff = DomainDiffUtil.Compare(before, after, analysisRun.Analysis);

        return new DomainOperabilityDelta(analysisRun, invalidity, diff);
    }
}