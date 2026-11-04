using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class SumEvalTests {
    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
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

    private const string BasketDsl = """
        domain Basket
        Line: entity { Price: Number }
        Order: entity {
          lines: many Line
          Total: policy { sum lines Price == 150 }
          Empty: policy { sum lines Price == 0 }
        }
        """;

    [Test]
    public async Task Sum_TwoLines_Is150() {
        var (domain, _) = Evolve(BasketDsl);
        var exec = ModuleExec.For(domain);
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var order = exec.Create("Order", new Dictionary<string, object?>()).Instance!;
        var first = exec.Create("Line", new Dictionary<string, object?> { ["Price"] = 100L }).Instance!;
        var second = exec.Create("Line", new Dictionary<string, object?> { ["Price"] = 50L }).Instance!;
        exec.Link("lines", order, first);
        exec.Link("lines", order, second);

        var policy = E("Order").Policies.First(p => p.Name == "Total");
        await Assert.That(exec.EvaluatePolicy(order, (policy).Name)).IsTrue();
    }

    [Test]
    public async Task Sum_Empty_IsZero() {
        var (domain, _) = Evolve(BasketDsl);
        var exec = ModuleExec.For(domain);
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var order = exec.Create("Order", new Dictionary<string, object?>()).Instance!;
        var policy = E("Order").Policies.First(p => p.Name == "Empty");
        await Assert.That(exec.EvaluatePolicy(order, (policy).Name)).IsTrue();
    }

    [Test]
    public async Task Sum_PrintsAddition() {
        var (domain, analysis) = Evolve(BasketDsl);
        var cs = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));
        var method = ExportedCSharp.ExtractMethod(cs, "bool Total(");
        await Assert.That(method).Contains("foreach");
        await Assert.That(method).Contains("Price");
        await Assert.That(method).Contains("+");
    }
}