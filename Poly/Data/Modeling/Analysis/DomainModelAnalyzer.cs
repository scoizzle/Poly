using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed class DomainModelAnalyzer {
    private readonly Analyzer _analyzer;

    public DomainModelAnalyzer()
        : this(new AnalyzerBuilder().UseIncrementalAnalysis().UseDomainModelValidation().Build()) {
    }

    internal DomainModelAnalyzer(Analyzer analyzer) {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
    }

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
        public AnalyzerBuilder UseDomainModelAnalysisPipeline() {
            builder.AddAnalyzer(new IncrementalAnalysisAnalyzer());
            builder.AddAnalyzer(new StructuralDomainAnalyzer());
            builder.AddAnalyzer(new SemanticDomainAnalyzer());
            builder.AddAnalyzer(new PolicyConstraintAnalyzer());
            builder.AddAnalyzer(new EffectBindingAnalyzer());
            return builder;
        }

        public AnalyzerBuilder UseDomainModelValidation() {
            return builder.UseDomainModelAnalysisPipeline();
        }
    }
}