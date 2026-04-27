namespace Poly.Data.Modeling.Analysis;

public sealed class DomainModelAnalyzerPipeline {
    private readonly IReadOnlyList<IDomainModelAnalyzer> _analyzers;

    public DomainModelAnalyzerPipeline(IEnumerable<IDomainModelAnalyzer> analyzers) {
        ArgumentNullException.ThrowIfNull(analyzers);
        _analyzers = analyzers.ToArray();
    }

    public DomainModelAnalysisResult Analyze(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        var context = new DomainModelAnalysisContext();

        foreach (var analyzer in _analyzers) {
            analyzer.Analyze(domain, context);
        }

        return new DomainModelAnalysisResult(context.Diagnostics.ToArray());
    }

    public static DomainModelAnalyzerPipeline CreateDefault() {
        return new DomainModelAnalyzerPipeline([
            new RelationshipAttachmentAnalyzer(),
            new StageLineageAnalyzer(),
            new EffectValidationAnalyzer()
        ]);
    }
}