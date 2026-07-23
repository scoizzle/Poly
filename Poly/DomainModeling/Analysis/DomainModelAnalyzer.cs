using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Static analyzer for V3 domain models. Uses a cached pipeline internally.
/// Thread-safe — the underlying passes are stateless.
/// </summary>
public static class DomainModelAnalyzer {
    private static readonly Analyzer _analyzer = new AnalyzerBuilder()
        .UseIncrementalAnalysis()
        .UseDomainModelValidation()
        .Build();

    public static AnalysisResult Analyze(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return _analyzer.Analyze(domain);
    }

    public static AnalysisResult Analyze(Domain domain, AnalysisResult priorAnalysis, IEnumerable<Node> invalidatedNodes) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(priorAnalysis);
        ArgumentNullException.ThrowIfNull(invalidatedNodes);
        return _analyzer.Analyze(domain, priorAnalysis, invalidatedNodes);
    }
}

public static class DomainModelAnalysisBuilderExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseDomainModelAnalysisPipeline() {
            builder.AddAnalyzer(new StructuralDomainAnalyzer());
            builder.AddAnalyzer(new SemanticDomainAnalyzer());
            builder.AddAnalyzer(new PolicyConstraintAnalyzer());
            builder.AddAnalyzer(new EffectAnalyzer());
            builder.AddAnalyzer(new ConstraintQualityAnalyzer());
            builder.AddAnalyzer(new EffectOrderingAnalyzer());
            builder.AddAnalyzer(new EnumConstraintSubsetAnalyzer());
            builder.AddAnalyzer(new CapabilityAnalyzer());
            builder.AddAnalyzer(new ConstraintPropagationAnalyzer());
            builder.AddAnalyzer(new RuleCoverageAnalyzer());
            builder.AddAnalyzer(new ContractIntegrationAnalyzer());
            builder.AddAnalyzer(new ActionParameterUsageAnalyzer());
            // Entity structure metadata (key, root, soft-delete, stages)
            builder.AddAnalyzer(new EntityStructureAnalyzer());
            // Stage-subscription analyzers (replaced retired event-centric passes)
            builder.AddAnalyzer(new SubscriptionContractAnalyzer());
            builder.AddAnalyzer(new SubscriptionCausalityAnalyzer());
            builder.AddAnalyzer(new SubscriptionReplaySafetyAnalyzer());
            // Authoring suggestions (advisory hints)
            builder.AddAnalyzer(new AuthoringSuggestionAnalyzer());
            // Entity Syntax projection (TypeDefinitionNode[] as metadata)
            builder.AddAnalyzer(new EntitySyntaxPass());
            return builder;
        }

        public AnalyzerBuilder UseDomainModelValidation() =>
            builder.UseDomainModelAnalysisPipeline();
    }
}