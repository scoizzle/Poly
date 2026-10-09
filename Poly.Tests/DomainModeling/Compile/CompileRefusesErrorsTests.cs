using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>G1: compile and simulate refuse an analysis with Errors (decision 7).</summary>
public class CompileRefusesErrorsTests {
    static (Domain Domain, AnalysisResult Analysis, string FirstError) DomainWithUnknownType() {
        var entity = new Entity("Order",
            [new Property("Name", new DomainTypeReference("Nope"), [])],
            [], [], []);
        var domain = ValidDomain.Create("Broken", [entity]);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        if (!analysis.HasErrors)
            throw new InvalidOperationException("Expected unknown-type domain to have analysis Errors.");
        var first = analysis.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error).Message;
        return (domain, analysis, first);
    }

    [Test]
    public async Task Lower_OnErrorAnalysis_ThrowsAndCatalogStaysEmpty() {
        var (domain, analysis, first) = DomainWithUnknownType();
        var session = DomainSession.Open(domain);
        await Assert.That(session.ArtifactCatalog.Artifacts).IsEmpty();

        var ex = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        await Assert.That(ex!.Message).IsEqualTo(first);
        await Assert.That(session.ArtifactCatalog.Artifacts).IsEmpty();
    }

    [Test]
    public async Task Emit_OnErrorAnalysis_ThrowsFirstErrorAndCatalogStaysEmpty() {
        var (domain, analysis, first) = DomainWithUnknownType();
        var session = DomainSession.Open(domain);

        var ex = Assert.Throws<InvalidOperationException>(() => session.Emit(domain, analysis));
        await Assert.That(ex!.Message).IsEqualTo(first);
        await Assert.That(session.ArtifactCatalog.Artifacts).IsEmpty();
    }

    [Test]
    public async Task ToSyntax_OnErrorAnalysis_Throws() {
        var (domain, analysis, first) = DomainWithUnknownType();

        var ex = Assert.Throws<InvalidOperationException>(
            () => DomainProgramProjection.ToSyntax(domain, analysis));
        await Assert.That(ex!.Message).IsEqualTo(first);
    }

    [Test]
    public async Task GetOrLower_OnErrorAnalysis_Throws() {
        var (domain, analysis, first) = DomainWithUnknownType();
        var session = DomainSession.Open(domain);

        var ex = Assert.Throws<InvalidOperationException>(
            () => RuntimeAnalysisCache.GetOrLower(domain, session, analysis));
        await Assert.That(ex!.Message).IsEqualTo(first);
    }

    [Test]
    public async Task Create_OnDomainWithErrors_Throws() {
        var (domain, _, first) = DomainWithUnknownType();
        var entity = domain.Types.OfType<Entity>().Single();

        var ex = Assert.Throws<InvalidOperationException>(
            () => DomainEntityInstance.Create(entity, null, domain));
        await Assert.That(ex!.Message).IsEqualTo(first);
    }

    [Test]
    public async Task CreateInstance_OnDomainWithErrors_ReportsFailure() {
        var (domain, analysis, first) = DomainWithUnknownType();
        var (sessionId, state) = McpSessionStore.Create("Broken");
        await Assert.That(McpSessionStore.Replace(sessionId, domain, analysis, state.Modeling)).IsTrue();

        var response = RuntimeTool.CreateInstance(sessionId, "Order");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains(first);
    }

    /// <summary>
    /// The 2-arg <c>ToSyntax(domain, INodeMetadataProvider)</c> has no HasErrors.
    /// Product callers reach the internal projection only through a checked door:
    /// the AnalysisResult overload (after <see cref="DomainModelAnalyzer.ThrowIfHasErrors"/>)
    /// or <see cref="RuntimeAnalysisCache.GetOrLower"/> (same check). This row pins that
    /// the AnalysisResult door is the one Export uses.
    /// </summary>
    [Test]
    public async Task Export_GoesThroughAnalysisResultToSyntax_WhichRefusesErrors() {
        var (domain, analysis, first) = DomainWithUnknownType();

        var ex = Assert.Throws<InvalidOperationException>(
            () => new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(ex!.Message).IsEqualTo(first);
    }
}