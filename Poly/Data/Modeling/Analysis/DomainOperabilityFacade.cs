using Poly.Data.Modeling.Analysis;
using Poly.Syntax.Analysis;

namespace Poly.Data.Modeling;

public sealed record DomainOperabilitySnapshot(
    AnalysisResult Analysis,
    NodeInvalidityReport Invalidity
);

public sealed record DomainOperabilityDelta(
    AnalysisResult Analysis,
    NodeInvalidityReport Invalidity,
    DomainDiffReport Diff
);

public static class DomainOperabilityFacade {
    public static DomainOperabilitySnapshot Capture(Domain domain, DomainModelAnalyzer? analyzer = null) {
        ArgumentNullException.ThrowIfNull(domain);

        var subjectAnalyzer = analyzer ?? new DomainModelAnalyzer();
        var analysis = subjectAnalyzer.Analyze(domain);
        var invalidity = DomainInvalidityExplainer.Explain(analysis);

        return new DomainOperabilitySnapshot(analysis, invalidity);
    }

    public static DomainOperabilityDelta AnalyzeExplainDiff(Domain before, Domain after, DomainModelAnalyzer? analyzer = null) {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var subjectAnalyzer = analyzer ?? new DomainModelAnalyzer();
        var analysis = subjectAnalyzer.Analyze(after);
        var invalidity = DomainInvalidityExplainer.Explain(analysis);
        var diff = DomainDiffUtil.Compare(before, after, analysis);

        return new DomainOperabilityDelta(analysis, invalidity, diff);
    }
}