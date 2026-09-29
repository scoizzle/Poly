using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class EnumMemberAgreeTests {
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
    public async Task StatusIsActive_AgreesOnSimulateAndPrintedCsharp() {
        var (domain, analysis) = Evolve("""
            domain Shop
            PatronStatus: enum { Active, Suspended }
            Patron: entity {
              Status: PatronStatus default(Active)
              IsActive: policy { Status is "Active" }
            }
            """);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var policy = entity.Policies.First(p => p.Name == "IsActive");
        var store = new DomainInstanceStore();
        var active = DomainEntityInstance.Create(entity, domain: domain);
        var suspended = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Status"] = "Suspended" }, domain);
        store.Add(active);
        store.Add(suspended);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var printedActive = ExportedCSharp.CreateEntity(asm, "Patron");
        var printedSuspended = ExportedCSharp.CreateEntity(asm, "Patron",
            ("status", Enum.Parse(asm.GetType("PatronStatus")!, "Suspended")));

        await Assert.That(active.EvaluatePolicy(policy)).IsTrue();
        await Assert.That((bool)printedActive.GetType().GetMethod("IsActive")!.Invoke(printedActive, null)!)
            .IsTrue();
        await Assert.That(suspended.EvaluatePolicy(policy)).IsFalse();
        await Assert.That((bool)printedSuspended.GetType().GetMethod("IsActive")!.Invoke(printedSuspended, null)!)
            .IsFalse();
    }
}
