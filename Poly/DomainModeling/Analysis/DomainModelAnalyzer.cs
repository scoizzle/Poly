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
        .UseDomainModelAnalysisPipeline()
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

    /// <summary>
    /// Analyzes <paramref name="domain"/> and requires a product catalog for
    /// non-failed trees. Prefer for runtime/export entrypoints that cannot proceed
    /// without <see cref="DomainCatalogMetadata"/>.
    /// </summary>
    public static AnalysisResult AnalyzeRequiringCatalog(Domain domain) {
        var analysis = Analyze(domain);
        RequireCatalog(analysis, domain);
        return analysis;
    }

    /// <summary>
    /// Fail closed when a non-failed analysis is missing <see cref="DomainCatalogMetadata"/>.
    /// Structural failures may omit the catalog without throwing (callers inspect diagnostics).
    /// </summary>
    public static void RequireCatalog(AnalysisResult analysis, Domain domain) {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(domain);
        if (analysis.HasStructuralFailure)
            return;
        if (analysis.GetCatalog(domain) is null)
            throw new InvalidOperationException(
                $"Domain analysis for '{domain.Name}' did not produce {nameof(DomainCatalogMetadata)}.");
    }

}

public static class DomainModelAnalysisBuilderExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseDomainModelAnalysisPipeline() {
            // Registration order must introduce each pass only after its declared
            // Dependencies are present (AnalyzerBuilder inserts after the last dep).
            // Fact emitters vs validate packs:
            //   RequiredPropertiesPass / EffectFactsPass publish bags;
            //   PolicyConstraintAnalyzer / EffectAnalyzer are diagnostic packs only.
            // Lint-only: Structural, PolicyConstraint, Effect, ConstraintQuality,
            // RuleCoverage, ContractIntegration, Subscription, AuthoringSuggestion.
            builder.AddAnalyzer(new StructuralDomainAnalyzer());
            builder.AddAnalyzer(new DomainCatalogPass());
            builder.AddAnalyzer(new RuntimeContractAnalyzer());
            builder.AddAnalyzer(new RequiredPropertiesPass());
            builder.AddAnalyzer(new PolicyConstraintAnalyzer());
            builder.AddAnalyzer(new ExpressionTypeAnalyzer());
            // DownstreamConstraintsMetadata consumed by EffectAnalyzer — register first
            builder.AddAnalyzer(new ConstraintPropagationAnalyzer());
            builder.AddAnalyzer(new EffectFactsPass());
            builder.AddAnalyzer(new EffectInvariantAnalyzer());
            builder.AddAnalyzer(new EffectAnalyzer());
            builder.AddAnalyzer(new ConstraintQualityAnalyzer());
            builder.AddAnalyzer(new CapabilityAnalyzer());
            builder.AddAnalyzer(new RuleCoverageAnalyzer());
            builder.AddAnalyzer(new ContractIntegrationAnalyzer());
            builder.AddAnalyzer(new EntityStructureAnalyzer());
            builder.AddAnalyzer(new SubscriptionAnalyzer());
            builder.AddAnalyzer(new EffectTopologyPass());
            builder.AddAnalyzer(new OwnershipAggregatePass());
            builder.AddAnalyzer(new CrossReferencePass());
            builder.AddAnalyzer(new StoragePass());
            builder.AddAnalyzer(new AuthoringSuggestionAnalyzer());
            // Entity Syntax projection is export-time only — not an analysis fact.
            return builder;
        }
    }
}