using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Compatibility door for tests. Product analyze is <see cref="DomainSession.Analyze"/>,
/// which binds the authoring session. This analyzes with the session bound to the
/// domain, or, when none is bound, a session loaded from the core catalog for the
/// domain's <c>uses</c> ids (an id the core catalog does not know throws).
/// </summary>
public static class DomainModelAnalyzer {
    /// <summary>
    /// Core's analysis passes in registration order: the schedule. The session
    /// owns them via <see cref="Compile.CoreLibrary"/>; libraries append flags
    /// after this list.
    /// </summary>
    internal static IReadOnlyList<INodeAnalyzer> CorePasses(
        ExpressionMeaning? meaning = null,
        ExpressionFormRegistry? forms = null) => [
        new StructuralDomainAnalyzer(),
        new DomainCatalogPass(),
        new ExpressionTypeAnalyzer(meaning, forms),
        new PolicyConstraintAnalyzer(),
        new ConstraintQualityAnalyzer(),
        new ContractIntegrationAnalyzer(),
        new RequiredPropertiesPass(),
        new ConstraintPropagationAnalyzer(),
        new EffectFactsPass(),
        new EffectInvariantAnalyzer(),
        new EntityStructureAnalyzer(),
        new EffectAnalyzer(),
        new RuntimeContractAnalyzer(),
        new CapabilityAnalyzer(),
        new SubscriptionAnalyzer(),
        new EffectTopologyPass(),
        new OwnershipAggregatePass(),
        new RuleCoverageAnalyzer(),
        new CrossReferencePass(),
        new UnwrittenRelationshipPass(),
        new AuthoringSuggestionAnalyzer(),
    ];

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

    /// <summary>
    /// Compile and simulate refuse an analysis with Errors (decision 7). Names the first
    /// error, matching the VM's <see cref="InvalidOperationException"/> style.
    /// </summary>
    public static void ThrowIfHasErrors(AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(analysis);
        if (!analysis.HasErrors)
            return;
        var first = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        throw new InvalidOperationException(first?.Message ?? "Domain analysis reported errors.");
    }

}

public static class DomainModelAnalysisBuilderExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseDomainModelAnalysisPipeline(
            ExpressionMeaning? meaning = null,
            ExpressionFormRegistry? forms = null) {
            // Registration order is the schedule. Waves are comments, not a scheduler.
            foreach (var pass in DomainModelAnalyzer.CorePasses(meaning, forms))
                builder.AddAnalyzer(pass);
            return builder;
        }
    }
}