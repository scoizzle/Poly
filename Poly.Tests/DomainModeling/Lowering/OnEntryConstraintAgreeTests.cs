using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class OnEntryConstraintAgreeTests {
    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics
                    .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        if (analysis.HasErrors)
            throw new InvalidOperationException(string.Join("; ",
                analysis.Diagnostics
                    .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, analysis);
    }

    [Test]
    public async Task OnEntryRangeViolation_FailsClosed_OnSimulateAndPrintedCsharp() {
        // Score is range(1, 10); the entry assigns from Bump, whose value is only
        // known at run time, so the violation is not a static-analysis error.
        var (domain, analysis) = Evolve("""
            domain Lab
            Widget: entity {
              Score: Number range(1, 10) default(5)
              Bump: Number default(100)
              Draft: stage {
                entry { assign Score to Bump }
              }
            }
            """);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Widget");

        await Assert.That(() => DomainEntityInstance.Create(entity, domain: domain))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("must be <= 10");

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var printedThrew = false;
        try {
            ExportedCSharp.CreateEntity(asm, "Widget");
        }
        catch (Exception ex) {
            printedThrew = true;
            await Assert.That(ex.ToString()).Contains("must be <= 10");
        }
        await Assert.That(printedThrew).IsTrue();
    }
}
