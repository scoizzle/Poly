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
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var store = new DomainInstanceStore();
        var order = DomainEntityInstance.Create(E("Order"), domain: domain);
        var first = DomainEntityInstance.Create(E("Line"),
            new Dictionary<string, object?> { ["Price"] = 100L }, domain);
        var second = DomainEntityInstance.Create(E("Line"),
            new Dictionary<string, object?> { ["Price"] = 50L }, domain);
        store.Add(order); store.Add(first); store.Add(second);
        store.Link("lines", order, first);
        store.Link("lines", order, second);

        var policy = E("Order").Policies.First(p => p.Name == "Total");
        await Assert.That(order.EvaluatePolicy(policy)).IsTrue();
    }

    [Test]
    public async Task Sum_Empty_IsZero() {
        var (domain, _) = Evolve(BasketDsl);
        Entity E(string n) => domain.Types.OfType<Entity>().First(e => e.Name == n);
        var store = new DomainInstanceStore();
        var order = DomainEntityInstance.Create(E("Order"), domain: domain);
        store.Add(order);

        var policy = E("Order").Policies.First(p => p.Name == "Empty");
        await Assert.That(order.EvaluatePolicy(policy)).IsTrue();
    }

    [Test]
    public async Task Sum_PrintsAddition() {
        var (domain, analysis) = Evolve(BasketDsl);
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        var method = ExportedCSharp.ExtractMethod(cs, "bool Total(");
        await Assert.That(method).Contains("foreach");
        await Assert.That(method).Contains("Price");
        await Assert.That(method).Contains("+");
    }
}