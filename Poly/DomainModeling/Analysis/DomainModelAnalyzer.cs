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
            builder.AddAnalyzer(new StructuralDomainAnalyzer());
            builder.AddAnalyzer(new SemanticDomainAnalyzer());
            builder.AddAnalyzer(new RuntimeContractAnalyzer());
            builder.AddAnalyzer(new PolicyConstraintAnalyzer());
            builder.AddAnalyzer(new EffectAnalyzer());
            builder.AddAnalyzer(new ConstraintQualityAnalyzer());

            builder.AddAnalyzer(new CapabilityAnalyzer());
            builder.AddAnalyzer(new ConstraintPropagationAnalyzer());
            builder.AddAnalyzer(new RuleCoverageAnalyzer());
            builder.AddAnalyzer(new ContractIntegrationAnalyzer());
            // Entity structure metadata (key, root, soft-delete, stages)
            builder.AddAnalyzer(new EntityStructureAnalyzer());
            // Stage-subscription validation (contract, causality, replay — unified in D2.5)
            builder.AddAnalyzer(new SubscriptionAnalyzer());
            // Cross-entity effect topology (create-in, invoke, subscriptions)
            builder.AddAnalyzer(new EffectTopologyPass());
            // Ownership hierarchy (roots, children, aggregate parents)
            builder.AddAnalyzer(new OwnershipAggregatePass());
            // Action metadata (parameters, return types, effective policies, transitions)
            builder.AddAnalyzer(new BehaviorPass());
            // Cross-entity dependency graph + cycle detection
            builder.AddAnalyzer(new CrossReferencePass());
            // Storage mapping (columns, navigations, FKs, keys, table names)
            builder.AddAnalyzer(new StoragePass());
            // Transport surface (exposable API roots and nesting)
            builder.AddAnalyzer(new TransportPass());
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