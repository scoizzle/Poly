using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Bootstrap;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Q2 — DomainCatalogPass structural fail-closed when Semantic DTLM/RLM are absent.
/// </summary>
public class DomainCatalogPassFailClosedTests {
    [Test]
    public async Task CatalogPass_WithoutSemanticBags_ReportsStructuralFailure_NoCatalog() {
        var order = new Entity("Order",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [], Policies: [],
            Stages: [new Stage("Draft", [], [], [], [])]);
        var domain = DomainTestFactory.Create("NoSemantic", [order], []);

        // Direct pass host: AnalyzerBuilder refuses CatalogPass without Semantic registered.
        // SUT is CatalogPass itself when DTLM/RLM are absent.
        var context = AnalysisContext.CreateDefault();
        new DomainCatalogPass().Analyze(context, domain);
        var analysis = new AnalysisResult(context, AnalysisTelemetry.Empty);

        await Assert.That(analysis.HasStructuralFailure).IsTrue();
        await Assert.That(analysis.GetCatalog(domain)).IsNull();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("Domain catalog requires", StringComparison.Ordinal))).IsTrue();

        // RequireCatalog must not throw over structural failure.
        DomainModelAnalyzer.RequireCatalog(analysis, domain);
    }

    [Test]
    public async Task CatalogPass_AfterSemantic_PublishesCatalog() {
        // DomainFactory includes built-in Text so Semantic does not structural-fail.
        var domain = DomainFactory.Create("WithSemantic", b =>
            b.AddEntity("Order")
             .AddPropertyToEntity("Order", new Property("Name", new DomainTypeReference("Text"), []))
             .AddStage("Order", "Draft"));

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new StructuralDomainAnalyzer())
            .AddAnalyzer(new SemanticDomainAnalyzer())
            .AddAnalyzer(new DomainCatalogPass())
            .Build()
            .Analyze(domain);

        await Assert.That(analysis.HasStructuralFailure).IsFalse();
        var catalog = analysis.GetCatalog(domain);
        await Assert.That(catalog).IsNotNull();
        await Assert.That(catalog!.ActionsByEntityName.ContainsKey("Order")).IsTrue();
    }
}