using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;

namespace Poly.Tests.DomainModeling.Analysis;

public class DomainCatalogPassFailClosedTests {
    [Test]
    public async Task CatalogPass_PublishesCatalog_WithoutPriorSemanticPass() {
        var domain = DomainFactory.Create("WithCatalog", b =>
            b.AddEntity("Order")
             .AddPropertyToEntity("Order", new Property("Name", new DomainTypeReference("Text"), []))
             .AddStage("Order", "Draft"));

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new StructuralDomainAnalyzer())
            .AddAnalyzer(new DomainCatalogPass())
            .Build()
            .Analyze(domain);

        await Assert.That(analysis.HasStructuralFailure).IsFalse();
        var catalog = analysis.GetCatalog(domain);
        await Assert.That(catalog).IsNotNull();
        await Assert.That(catalog!.ActionsByEntityName.ContainsKey("Order")).IsTrue();
        await Assert.That(ReferenceEquals(analysis.GetTypeLookup(domain), catalog.Types)).IsTrue();
        await Assert.That(ReferenceEquals(analysis.GetTypeLookup(), catalog.Types)).IsTrue();
    }

    [Test]
    public async Task CatalogPass_DirectAnalyze_PublishesOnDomain() {
        var order = new Entity("Order",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [], Policies: [],
            Stages: [new Stage("Draft", [], [], [], [])]);
        var domain = DomainTestFactory.Create("Direct", [order], []);

        var context = AnalysisContext.CreateDefault();
        new DomainCatalogPass().Analyze(context, domain);
        var analysis = new AnalysisResult(context, AnalysisTelemetry.Empty);

        await Assert.That(analysis.GetCatalog(domain)).IsNotNull();
        DomainModelAnalyzer.RequireCatalog(analysis, domain);
    }
}