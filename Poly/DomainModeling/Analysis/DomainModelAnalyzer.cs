using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Static analyzer for V3 domain models. Uses a cached pipeline internally
/// for all analysis entry points.
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
            // Registration order must introduce each pass only after its declared
            // Dependencies are present (AnalyzerBuilder inserts after the last dep).
            // Fact emitters vs validate packs (DAS W3.2):
            //   RequiredPropertiesPass / EffectFactsPass publish bags;
            //   PolicyConstraintAnalyzer / EffectAnalyzer are diagnostic packs only.
            // Lint-only: Structural, PolicyConstraint, Effect, ConstraintQuality,
            // RuleCoverage, ContractIntegration, Subscription, AuthoringSuggestion.
            builder.AddAnalyzer(new StructuralDomainAnalyzer());
            builder.AddAnalyzer(new SemanticDomainAnalyzer());
            builder.AddAnalyzer(new RuntimeContractAnalyzer());
            // Sole name→member catalog publisher (DAS W1.4)
            builder.AddAnalyzer(new DomainCatalogPass());
            builder.AddAnalyzer(new RequiredPropertiesPass());
            builder.AddAnalyzer(new PolicyConstraintAnalyzer());
            // DownstreamConstraintsMetadata consumed by EffectAnalyzer — register first
            builder.AddAnalyzer(new ConstraintPropagationAnalyzer());
            builder.AddAnalyzer(new EffectFactsPass());
            builder.AddAnalyzer(new EffectAnalyzer());
            builder.AddAnalyzer(new ConstraintQualityAnalyzer());
            builder.AddAnalyzer(new CapabilityAnalyzer());
            builder.AddAnalyzer(new RuleCoverageAnalyzer());
            builder.AddAnalyzer(new ContractIntegrationAnalyzer());
            builder.AddAnalyzer(new EntityStructureAnalyzer());
            builder.AddAnalyzer(new SubscriptionAnalyzer());
            builder.AddAnalyzer(new EffectTopologyPass());
            builder.AddAnalyzer(new OwnershipAggregatePass());
            builder.AddAnalyzer(new BehaviorPass());
            builder.AddAnalyzer(new CrossReferencePass());
            builder.AddAnalyzer(new StoragePass());
            builder.AddAnalyzer(new TransportPass());
            builder.AddAnalyzer(new AuthoringSuggestionAnalyzer());
            // Entity Syntax projection is export-time only (DAS W0) — not an analysis fact.
            return builder;
        }

        public AnalyzerBuilder UseDomainModelValidation() =>
            builder.UseDomainModelAnalysisPipeline();
    }
}