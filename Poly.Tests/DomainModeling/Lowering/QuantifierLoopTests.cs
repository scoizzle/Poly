using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

public class QuantifierLoopTests {
    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        if (analysis.HasErrors)
            throw new InvalidOperationException(string.Join("; ",
                analysis.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, analysis);
    }

    private const string BasketDsl = """
        domain Basket
        Line: entity {
          Qty: Number
          Open: Boolean
        }
        Order: entity {
          lines: many Line
          AnyOpen: policy { any lines where Open }
          AllOpen: policy { all lines where Open }
          NoneOpen: policy { none lines where Open }
          OpenCount: policy { count lines where Open is true == 2 }
        }
        """;

    [Test]
    public async Task Simulate_All_EmptyCollection_IsFalse() {
        var (domain, _) = Evolve(BasketDsl);
        var order = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var store = new DomainInstanceStore();
        var inst = DomainEntityInstance.Create(order, domain: domain);
        store.Add(inst);
        await Assert.That(inst.EvaluatePolicy(order.Policies.First(p => p.Name == "AllOpen"))).IsFalse();
        await Assert.That(inst.EvaluatePolicy(order.Policies.First(p => p.Name == "AnyOpen"))).IsFalse();
        await Assert.That(inst.EvaluatePolicy(order.Policies.First(p => p.Name == "NoneOpen"))).IsTrue();
    }

    [Test]
    public async Task Simulate_None_And_CountWhere_MatchLinkedLines() {
        var (domain, _) = Evolve(BasketDsl);
        var orderEnt = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var lineEnt = domain.Types.OfType<Entity>().First(e => e.Name == "Line");
        var store = new DomainInstanceStore();
        var order = DomainEntityInstance.Create(orderEnt, domain: domain);
        var openA = DomainEntityInstance.Create(lineEnt,
            new Dictionary<string, object?> { ["Qty"] = 1L, ["Open"] = true }, domain);
        var openB = DomainEntityInstance.Create(lineEnt,
            new Dictionary<string, object?> { ["Qty"] = 2L, ["Open"] = true }, domain);
        var closed = DomainEntityInstance.Create(lineEnt,
            new Dictionary<string, object?> { ["Qty"] = 3L, ["Open"] = false }, domain);
        store.Add(order); store.Add(openA); store.Add(openB); store.Add(closed);
        store.Link("lines", order, openA);
        store.Link("lines", order, openB);
        store.Link("lines", order, closed);

        await Assert.That(order.EvaluatePolicy(orderEnt.Policies.First(p => p.Name == "AnyOpen"))).IsTrue();
        await Assert.That(order.EvaluatePolicy(orderEnt.Policies.First(p => p.Name == "AllOpen"))).IsFalse();
        await Assert.That(order.EvaluatePolicy(orderEnt.Policies.First(p => p.Name == "NoneOpen"))).IsFalse();
        await Assert.That(order.EvaluatePolicy(orderEnt.Policies.First(p => p.Name == "OpenCount"))).IsTrue();
    }

    [Test]
    public async Task Export_QuantifierBody_ComparesTargetEnumNotSourceEnum() {
        var (domain, analysis) = Evolve("""
            domain Shop
            PatronStatus: enum { Active, Closed }
            LoanStatus: enum { Current, Overdue }
            Loan: entity {
              Status: LoanStatus
            }
            Patron: entity {
              Status: PatronStatus
              loans: many Loan
              HasOverdue: policy { any loans where Status is "Overdue" }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);
        var start = cs.IndexOf("bool HasOverdue(", StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThanOrEqualTo(0);
        var brace = cs.IndexOf('{', start);
        var depth = 0;
        var end = brace;
        for (var i = brace; i < cs.Length; i++) {
            if (cs[i] == '{') depth++;
            else if (cs[i] == '}') {
                depth--;
                if (depth == 0) { end = i + 1; break; }
            }
        }
        var method = cs[start..end];
        await Assert.That(method).Contains("LoanStatus.Overdue");
        await Assert.That(method).DoesNotContain("PatronStatus.Overdue");
        await Assert.That(method).Contains("foreach");
    }

    [Test]
    public async Task AnyExpr_LowersToForeachOverNavigation() {
        var (domain, analysis) = Evolve(BasketDsl);
        var order = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var policy = order.Policies.First(p => p.Name == "AnyOpen");
        var body = DomainToCSharpExporter.LowerExpressionToMethodBody(
            policy.Expression, order, domain, analysis);
        await Assert.That(body).IsNotNull();
        var loop = Flatten(body!).OfType<ForEachLoop>().FirstOrDefault();
        await Assert.That(loop).IsNotNull();
        await Assert.That(loop!.Collection).IsTypeOf<Member>();
        await Assert.That(((Member)loop.Collection).MemberName).IsEqualTo("Lines");
    }

    private static IEnumerable<Node> Flatten(Node node) {
        yield return node;
        foreach (var child in node.Children) {
            if (child is null) continue;
            foreach (var n in Flatten(child))
                yield return n;
        }
    }
}
