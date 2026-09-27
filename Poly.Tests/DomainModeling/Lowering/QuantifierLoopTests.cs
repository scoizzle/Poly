using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

public class QuantifierLoopTests {
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
        Line: entity {
          Qty: Number
          Open: Boolean
          parts: many Part
        }
        Order: entity {
          lines: many Line
          AnyOpen: policy { any lines where Open }
          AllOpen: policy { all lines where Open }
          NoneOpen: policy { none lines where Open }
          OpenCount: policy { count lines where Open is true == 2 }
          Ready: Boolean
          ReadyWithOpenLine: policy { Ready and (any lines where Open) }
          AnyOpenLineWithBigPart: policy { any lines where Open and (any parts where Qty > 5) }
        }
        Part: entity {
          Qty: Number
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

    [Test]
    public async Task Quantifiers_GiveSameAnswers_SimulatedAndInExportedCSharp() {
        var (domain, analysis) = Evolve(BasketDsl);
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);
        var policyNames = E("Order").Policies.Select(p => p.Name).ToArray();

        // Simulated: instances linked through a store.
        var store = new DomainInstanceStore();
        DomainEntityInstance SimPart(long qty) {
            var part = DomainEntityInstance.Create(E("Part"), new Dictionary<string, object?> { ["Qty"] = qty }, domain);
            store.Add(part);
            return part;
        }
        DomainEntityInstance SimLine(bool open, params DomainEntityInstance[] parts) {
            var line = DomainEntityInstance.Create(E("Line"),
                new Dictionary<string, object?> { ["Qty"] = 1L, ["Open"] = open }, domain);
            store.Add(line);
            foreach (var part in parts) store.Link("parts", line, part);
            return line;
        }
        DomainEntityInstance SimOrder(bool ready, params DomainEntityInstance[] lines) {
            var order = DomainEntityInstance.Create(E("Order"),
                new Dictionary<string, object?> { ["Ready"] = ready }, domain);
            store.Add(order);
            foreach (var line in lines) store.Link("lines", order, line);
            return order;
        }

        // Printed: the same shapes built from the exported, compiled types.
        var asm = CompileAndLoad(new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        object Make(string type, params (string Name, object? Value)[] args) {
            var create = asm.GetType(type)!.GetMethods()
                .Where(m => m.Name == "Create" && m.IsStatic)
                .OrderByDescending(m => m.GetParameters().Length)
                .First();
            var values = create.GetParameters().Select(p =>
                args.FirstOrDefault(a => string.Equals(a.Name, p.Name, StringComparison.OrdinalIgnoreCase)) is { Name: not null } a
                    ? a.Value
                    : p.HasDefaultValue ? p.DefaultValue : null).ToArray();
            var result = create.Invoke(null, values)!;
            return result.GetType().GetProperty("Value")!.GetValue(result)!;
        }
        Array ListOf(string type, object[] items) {
            var array = Array.CreateInstance(asm.GetType(type)!, items.Length);
            items.CopyTo(array, 0);
            return array;
        }
        object PrintPart(long qty) => Make("Part", ("qty", qty));
        object PrintLine(bool open, params object[] parts) =>
            Make("Line", ("qty", 1L), ("open", open), ("parts", ListOf("Part", parts)));
        object PrintOrder(bool ready, params object[] lines) =>
            Make("Order", ("ready", ready), ("lines", ListOf("Line", lines)));

        var cases = new (string Name, DomainEntityInstance Simulated, object Printed)[] {
            ("no lines", SimOrder(true), PrintOrder(true)),
            ("mixed lines", SimOrder(true, SimLine(true, SimPart(9)), SimLine(true), SimLine(false)),
                PrintOrder(true, PrintLine(true, PrintPart(9)), PrintLine(true), PrintLine(false))),
            ("all open, small parts, not ready", SimOrder(false, SimLine(true, SimPart(1)), SimLine(true)),
                PrintOrder(false, PrintLine(true, PrintPart(1)), PrintLine(true))),
        };
        var bigPart = E("Order").Policies.First(p => p.Name == "AnyOpenLineWithBigPart");
        await Assert.That(cases[1].Simulated.EvaluatePolicy(bigPart)).IsTrue();
        await Assert.That(cases[2].Simulated.EvaluatePolicy(bigPart)).IsFalse();

        foreach (var (name, simulated, printed) in cases) {
            foreach (var policyName in policyNames) {
                var sim = simulated.EvaluatePolicy(E("Order").Policies.First(p => p.Name == policyName));
                var print = (bool)printed.GetType().GetMethod(policyName)!.Invoke(printed, null)!;
                await Assert.That(print).IsEqualTo(sim).Because($"{policyName} on {name}");
            }
        }
    }

    private static System.Reflection.Assembly CompileAndLoad(string cs) {
        var tree = CSharpSyntaxTree.ParseText("#nullable enable\n" + cs);
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("QuantifierLoops", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var pe = new MemoryStream();
        var emit = compilation.Emit(pe);
        if (!emit.Success)
            throw new InvalidOperationException(string.Join("\n", emit.Diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)) + "\n" + cs);
        pe.Position = 0;
        return new System.Runtime.Loader.AssemblyLoadContext("QuantifierLoops", isCollectible: true).LoadFromStream(pe);
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
