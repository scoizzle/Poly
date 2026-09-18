using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Compatibility door for tests. Product analyze is <see cref="DomainSession.Analyze"/>,
/// which binds the authoring session. This forwards to the cache (bound session
/// if Analyze already ran; otherwise a core-catalog fallback).
/// </summary>
public static class DomainModelAnalyzer {
    /// <summary>
    /// Builds the product analysis pipeline. The session owns its analyzer;
    /// this factory is the single construction point. Storage mapping is a
    /// library overlay, not a core-list pass.
    /// </summary>
    internal static Analyzer BuildPipeline(
        IReadOnlyList<INodeAnalyzer>? extraAnalyzers = null,
        ExpressionMeaning? meaning = null,
        ExpressionFormRegistry? forms = null) {
        var builder = new AnalyzerBuilder()
            .UseDomainModelAnalysisPipeline(meaning, forms);
        if (extraAnalyzers is not null) {
            foreach (var analyzer in extraAnalyzers)
                builder.AddAnalyzer(analyzer);
        }
        return builder.Build();
    }

    public static AnalysisResult Analyze(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return RuntimeAnalysisCache.Session(domain).Analyze(domain);
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
    /// Fail closed when a successful analysis is missing <see cref="DomainCatalogMetadata"/>.
    /// Analyses that already have errors may omit the catalog; callers inspect diagnostics.
    /// </summary>
    public static void RequireCatalog(AnalysisResult analysis, Domain domain) {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(domain);
        // PR 68: HasStructuralFailure collapsed into HasErrors — same early-return.
        if (analysis.HasErrors)
            return;
        if (analysis.GetCatalog(domain) is null)
            throw new InvalidOperationException(
                $"Domain analysis for '{domain.Name}' did not produce {nameof(DomainCatalogMetadata)}.");
    }

}

public static class DomainModelAnalysisBuilderExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseDomainModelAnalysisPipeline(
            ExpressionMeaning? meaning = null,
            ExpressionFormRegistry? forms = null) {
            // Registration order is the schedule. Waves are comments, not a scheduler.
            // Libraries append flags after this list; they fill ExpressionMeaning, not mid-list passes.
            builder.AddAnalyzer(new StructuralDomainAnalyzer());
            builder.AddAnalyzer(new DomainCatalogPass());
            builder.AddAnalyzer(new ExpressionTypeAnalyzer(meaning, forms));
            builder.AddAnalyzer(new PolicyConstraintAnalyzer());
            builder.AddAnalyzer(new ConstraintQualityAnalyzer());
            builder.AddAnalyzer(new ContractIntegrationAnalyzer());
            builder.AddAnalyzer(new RequiredPropertiesPass());
            builder.AddAnalyzer(new ConstraintPropagationAnalyzer());
            builder.AddAnalyzer(new EffectFactsPass());
            builder.AddAnalyzer(new EffectInvariantAnalyzer());
            builder.AddAnalyzer(new EffectAnalyzer());
            builder.AddAnalyzer(new RuntimeContractAnalyzer());
            builder.AddAnalyzer(new CapabilityAnalyzer());
            builder.AddAnalyzer(new SubscriptionAnalyzer());
            builder.AddAnalyzer(new EntityStructureAnalyzer());
            builder.AddAnalyzer(new EffectTopologyPass());
            builder.AddAnalyzer(new OwnershipAggregatePass());
            builder.AddAnalyzer(new RuleCoverageAnalyzer());
            builder.AddAnalyzer(new CrossReferencePass());
            builder.AddAnalyzer(new AuthoringSuggestionAnalyzer());
            return builder;
        }
    }
}