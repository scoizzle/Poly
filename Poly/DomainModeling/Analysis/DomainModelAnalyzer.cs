using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

public sealed class DomainModelAnalyzer {
    private readonly Analyzer _analyzer;

    public DomainModelAnalyzer(AnalysisOptions? options = null) {
        _analyzer = new AnalyzerBuilder()
            .UseIncrementalAnalysis()
            .UseV3DomainModelValidation()
            .WithOptions(options ?? AnalysisOptions.Default)
            .Build();
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
        public AnalyzerBuilder UseV3DomainModelAnalysisPipeline() {
            builder.AddAnalyzer(new StructuralDomainAnalyzer());
            builder.AddAnalyzer(new SemanticDomainAnalyzer());
            builder.AddAnalyzer(new PolicyConstraintAnalyzer());
            builder.AddAnalyzer(new EffectAnalyzer());
            builder.AddAnalyzer(new ConstraintQualityAnalyzer());
            builder.AddAnalyzer(new EffectOrderingAnalyzer());
            builder.AddAnalyzer(new EventFlowAnalyzer());
            builder.AddAnalyzer(new ReplaySafetyAnalyzer());
            builder.AddAnalyzer(new CorrelationAnalyzer());
            builder.AddAnalyzer(new CausalityAnalyzer());
            builder.AddAnalyzer(new EnumConstraintSubsetAnalyzer());
            builder.AddAnalyzer(new CapabilityAnalyzer());
            builder.AddAnalyzer(new EventContractAnalyzer());
            builder.AddAnalyzer(new ConstraintPropagationAnalyzer());
            builder.AddAnalyzer(new RuleCoverageAnalyzer());
            builder.AddAnalyzer(new ContractIntegrationAnalyzer());
            builder.AddAnalyzer(new ActionParameterUsageAnalyzer());
            return builder;
        }

        public AnalyzerBuilder UseV3DomainModelValidation() =>
            builder.UseV3DomainModelAnalysisPipeline();
    }
}