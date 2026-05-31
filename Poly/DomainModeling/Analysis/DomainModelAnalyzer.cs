using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

public sealed class DomainModelAnalyzer {
    private readonly Analyzer _analyzer = new AnalyzerBuilder()
        .UseIncrementalAnalysis()
        .UseV3DomainModelValidation()
        .Build();

    public AnalysisResult Analyze(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return _analyzer.Analyze(domain);
    }

    public AnalysisResult Analyze(Domain domain, AnalysisResult priorAnalysis, IEnumerable<Node> invalidatedNodes) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(priorAnalysis);
        ArgumentNullException.ThrowIfNull(invalidatedNodes);
        return _analyzer.Analyze(domain, priorAnalysis, invalidatedNodes);
    }
}

public static class DomainModelAnalysisBuilderExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseV3DomainModelAnalysisPipeline() {
            builder.AddAnalyzer(new StructuralDomainAnalyzer());
            builder.AddAnalyzer(new SemanticDomainAnalyzer());
            return builder;
        }

        public AnalyzerBuilder UseV3DomainModelValidation() =>
            builder.UseV3DomainModelAnalysisPipeline();
    }
}