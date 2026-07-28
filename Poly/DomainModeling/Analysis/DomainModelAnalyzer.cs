using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Static analyzer for V3 domain models. Uses a cached pipeline internally
/// for the default (no-authoring-context) path. When a <see cref="DomainAuthoringContext"/>
/// is provided, a fresh pipeline is built per call to allow pack-contributed
/// passes to participate in domain analysis.
/// Thread-safe — the underlying passes are stateless.
/// </summary>
public static class DomainModelAnalyzer {
    private static readonly Analyzer _analyzer = new AnalyzerBuilder()
        .UseIncrementalAnalysis()
        .UseDomainModelValidation()
        .Build();

    /// <summary>Builds a domain analyzer pipeline, optionally configured with pack passes from the authoring context.</summary>
    internal static Analyzer BuildDomainAnalyzer(DomainAuthoringContext? authoring = null) {
        if (authoring is null)
            return _analyzer;

        var builder = new AnalyzerBuilder()
            .UseIncrementalAnalysis()
            .UseDomainModelAnalysisPipeline(authoring);

        foreach (var pass in authoring.Passes.Build())
            builder.AddAnalyzer(pass);

        return builder.Build();
    }

    public static AnalysisResult Analyze(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return _analyzer.Analyze(domain);
    }

    public static AnalysisResult Analyze(Domain domain, DomainAuthoringContext? authoring) {
        ArgumentNullException.ThrowIfNull(domain);
        var analyzer = BuildDomainAnalyzer(authoring);
        return analyzer.Analyze(domain);
    }

    public static AnalysisResult Analyze(Domain domain, AnalysisResult priorAnalysis, IEnumerable<Node> invalidatedNodes) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(priorAnalysis);
        ArgumentNullException.ThrowIfNull(invalidatedNodes);
        return _analyzer.Analyze(domain, priorAnalysis, invalidatedNodes);
    }

    public static AnalysisResult Analyze(Domain domain, DomainAuthoringContext? authoring, AnalysisResult priorAnalysis, IEnumerable<Node> invalidatedNodes) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(priorAnalysis);
        ArgumentNullException.ThrowIfNull(invalidatedNodes);
        var analyzer = BuildDomainAnalyzer(authoring);
        return analyzer.Analyze(domain, priorAnalysis, invalidatedNodes);
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

        /// <summary>Builds the pipeline with pack-aware storage configuration.</summary>
        public AnalyzerBuilder UseDomainModelAnalysisPipeline(DomainAuthoringContext authoring) {
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
            builder.AddAnalyzer(new EntityStructureAnalyzer());
            builder.AddAnalyzer(new SubscriptionAnalyzer());
            builder.AddAnalyzer(new EffectTopologyPass());
            builder.AddAnalyzer(new OwnershipAggregatePass());
            builder.AddAnalyzer(new BehaviorPass());
            builder.AddAnalyzer(new CrossReferencePass());
            builder.AddAnalyzer(new StoragePass(
                typeMaps: authoring.TypeMaps,
                conventions: authoring.StorageConventions));
            builder.AddAnalyzer(new TransportPass());
            builder.AddAnalyzer(new AuthoringSuggestionAnalyzer());
            builder.AddAnalyzer(new EntitySyntaxPass());
            return builder;
        }

        public AnalyzerBuilder UseDomainModelValidation() =>
            builder.UseDomainModelAnalysisPipeline();
    }
}