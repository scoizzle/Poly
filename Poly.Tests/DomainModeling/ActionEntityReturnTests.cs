using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;

using DmAction = Poly.DomainModeling.Action;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// P3: action <c>-&gt; Entity</c> requires create producer; runtime returns created instance.
/// </summary>
public class ActionEntityReturnTests {
    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(new Domain("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        return (result.Root!, analysis);
    }

    [Test]
    public async Task Analyze_ReturnTypeWithoutCreate_ReportsDMEFF009() {
        var draft = new Stage("Draft", [], [], [], []);
        var done = new Stage("Done", [], [], [], []);
        var place = new DmAction(
            "Place",
            new InvocationResult([new InvocationResult.Member("Instance", new DomainTypeReference("Order"), [])]),
            [],
            [new StageTransitionEffect(new StageReference("Done"))],
            []);
        var order = new Entity("Order", [], [place], [], [draft, done]);
        var domain = new Domain("RetNoCreate", [order], []);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var errors = analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        await Assert.That(errors.Any(d =>
            d.Message.Contains("declares return type", StringComparison.Ordinal)
            && d.Message.Contains("no create", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task Analyze_PrimitiveReturn_ReportsNotSupported() {
        var x = new Property("X", new DomainTypeReference("Number"), []);
        var compute = new DmAction(
            "Compute",
            new InvocationResult([new InvocationResult.Member("Value", new DomainTypeReference("Number"), [])]),
            [],
            [new AssignEffect(DomainExpression.Property("X"), DomainExpression.Literal(1L))],
            []);
        var order = new Entity("Order", [x], [compute], [], []);
        var domain = DomainFactory.Create("PrimRet");
        domain = domain with {
            Types = domain.Types.Concat([order]).ToList()
        };
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var errors = analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        await Assert.That(errors.Any(d =>
            d.Message.Contains("only entity", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task Invoke_CreateInWithReturnType_ReturnsCreatedInstance() {
        var (domain, analysis) = Evolve("""
            domain RetCreate
            Customer: entity {
              Name: Text
              orders: many Order
              PlaceOrder: action -> Order {
                create in orders { Code: "O1" }
              }
              Active: stage {}
            }
            Order: entity {
              Code: Text
              Draft: stage {}
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();

        var store = new DomainInstanceStore();
        var customerEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var customer = DomainEntityInstance.Create(customerEntity,
            new Dictionary<string, object?> { ["Name"] = "A" }, domain);
        store.Add(customer);

        var result = customer.InvokeAction("PlaceOrder");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.ResultTypeName).IsEqualTo("Order");
        await Assert.That(result.ResultInstance).IsNotNull();
        await Assert.That(result.ResultInstance!.Entity.Name).IsEqualTo("Order");
        await Assert.That(result.ResultInstance.GetProperty<string>("Code")).IsEqualTo("O1");
    }

    [Test]
    public async Task Analyze_CreateInReturn_HappyPath_NoError() {
        var (_, analysis) = Evolve("""
            domain RetOk
            Customer: entity {
              orders: many Order
              PlaceOrder: action -> Order {
                create in orders { }
              }
            }
            Order: entity { Draft: stage {} }
            """);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("declares return type", StringComparison.Ordinal))).IsFalse();
    }
}