using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Builds the analysis and domain a <see cref="LoweringContext"/> now requires, so a test that
/// lowers one hand-built entity or expression does not repeat the setup.
/// </summary>
static class LoweringFixtures {
    /// <summary>A domain holding <paramref name="entities"/> and its analysis.</summary>
    public static (Domain Domain, AnalysisResult Analysis) Analyze(params Entity[] entities) {
        var domain = ValidDomain.Create("Fixture", entities);
        return (domain, DomainModelAnalyzer.Analyze(domain));
    }

    /// <summary>A context over the domain of <paramref name="entities"/>.</summary>
    public static LoweringContext Context(Node subject, params Entity[] entities) {
        var (domain, analysis) = Analyze(entities);
        return new LoweringContext(subject, analysis, domain);
    }

    /// <summary>
    /// A context over a domain with one empty entity, for a test that lowers an expression
    /// and needs no entity of its own.
    /// </summary>
    public static LoweringContext MinimalContext(Node subject) =>
        Context(subject, Placeholder());

    /// <summary>
    /// As <see cref="MinimalContext(Node)"/>, with the domain bound to <paramref name="session"/>
    /// so the pass reads that session's meaning and forms, not the ones the domain's own
    /// extensions would load.
    /// </summary>
    public static LoweringContext MinimalContext(Node subject, DomainSession session) {
        var domain = ValidDomain.Create("Fixture", [Placeholder()]);
        var analysis = session.Analyze(domain);
        return new LoweringContext(subject, analysis, domain);
    }

    static Entity Placeholder() => new("Placeholder", Properties: [], Actions: [], Policies: [], Stages: []);
}