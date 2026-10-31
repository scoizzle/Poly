using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// S5: one VM analysis per domain and domain analysis on a session.
/// <see cref="DomainSession.VmAnalysisCount"/> is the proof a second Emit did not analyze again.
/// </summary>
public sealed class EmitTwiceVmAnalysisTests {
    private const string Spotless = """
        domain Spotless
        Item: entity {
          Name: Text
          Open: stage { }
        }
        """;

    private const string Beta = """
        domain Beta
        Item: entity {
          Name: Text
        }
        """;

    [Test]
    public async Task Emit_Twice_ReusesVmAnalysis() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Spotless);

        var first = session.Emit(domain, analysis);
        var second = session.Emit(domain, analysis);

        await Assert.That(session.VmAnalysisCount).IsEqualTo(1);
        await Assert.That(first.Count).IsGreaterThan(0);
        await Assert.That(second.Count).IsEqualTo(first.Count);
        var report = session.ArtifactCatalog.Find(ArtifactId.Create([domain.Name], "vm-analysis-report"));
        await Assert.That(report).IsNotNull();
        await Assert.That(report!.Descriptor.Producer).IsEqualTo("Emit");
    }

    [Test]
    public async Task Emit_DifferentDomainAnalysis_AnalyzesAgain() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Spotless);
        session.Emit(domain, analysis);
        var again = session.Analyze(domain);
        session.Emit(domain, again);

        await Assert.That(ReferenceEquals(analysis, again)).IsFalse();
        await Assert.That(session.VmAnalysisCount).IsEqualTo(2);
    }

    [Test]
    public async Task Emit_DifferentDomain_AnalyzesAgain() {
        var session = DomainSession.ForSource(Spotless, ExtensionCatalog.ProductAuthoring);
        var alpha = Apply(session, Spotless);
        var other = Apply(session, Beta);

        session.Emit(alpha.Root, alpha.Analysis);
        session.Emit(other.Root, other.Analysis);

        await Assert.That(session.VmAnalysisCount).IsEqualTo(2);
    }

    [Test]
    public async Task Lower_DoesNotRunVmAnalysis() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Spotless);
        session.Lower(domain, analysis);

        await Assert.That(session.VmAnalysisCount).IsEqualTo(0);
        await Assert.That(session.ArtifactCatalog.Find(ArtifactId.Create(["Spotless"], "vm-analysis-report")))
            .IsNull();
    }

    private static EvolutionResult Apply(DomainSession session, string poly) {
        var result = new DomainEvolution(new Domain("_", [])).Apply(
            new PolyDslParser(poly, session).Parse(), session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary ?? "evolution rejected the domain");
        return result;
    }
}