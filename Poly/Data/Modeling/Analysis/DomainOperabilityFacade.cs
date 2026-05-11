using Poly.Data.Modeling.Analysis;

namespace Poly.Data.Modeling;

public sealed record DomainOperabilitySnapshot(
    AnalysisResult Analysis
);

public sealed record DomainOperabilityDelta(
    AnalysisResult Analysis,
    DomainDiffReport Diff
);

public static class DomainOperabilityFacade {
    public static DomainOperabilitySnapshot Capture(Domain domain, DomainModelAnalyzer? analyzer = null) {
        ArgumentNullException.ThrowIfNull(domain);

        var subjectAnalyzer = analyzer ?? new DomainModelAnalyzer();
        var analysis = subjectAnalyzer.Analyze(domain);

        return new DomainOperabilitySnapshot(analysis);
    }

    public static DomainOperabilityDelta AnalyzeExplainDiff(Domain before, Domain after, DomainModelAnalyzer? analyzer = null) {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var subjectAnalyzer = analyzer ?? new DomainModelAnalyzer();
        var analysis = subjectAnalyzer.Analyze(after);
        var diff = DomainDiffUtil.Compare(before, after, analysis);

        return new DomainOperabilityDelta(analysis, diff);
    }
}