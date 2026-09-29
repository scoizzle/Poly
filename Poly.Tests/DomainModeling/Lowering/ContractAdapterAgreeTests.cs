using System.Reflection;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class ContractAdapterAgreeTests {
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
    public async Task UnboundContractAdapter_FailsClosed_OnSimulateAndPrintedCsharp() {
        var (domain, analysis) = Evolve("""
            domain Shop
            Order: entity {
              Total: Number default(0)
              Pay: action (request: ChargeRequest) {
                assign Total to Total
              }
            }

            Stripe: contract external stripe v1 {
              ChargeRequest: value {
                Amount: Number
                Currency: Text
              }
              Charge: outbound operation ChargeRequest
            }

            ChargeOrder: bind Stripe Charge to Pay request
            """);
        var orderE = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var store = new DomainInstanceStore();
        var order = DomainEntityInstance.Create(orderE, domain: domain);
        store.Add(order);
        var request = new Dictionary<string, object?> { ["Amount"] = 10L, ["Currency"] = "USD" };
        var sim = order.InvokeAction("Pay",
            new Dictionary<string, object?> { ["request"] = request });
        await Assert.That(sim.Succeeded).IsFalse();
        await Assert.That(sim.ErrorMessage).Contains("Stripe.Charge");
        await Assert.That(sim.ErrorMessage!).Contains("no in-process adapter");

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var printed = ExportedCSharp.CreateEntity(asm, "Order");
        var reqType = asm.GetType("ChargeRequest")
            ?? throw new InvalidOperationException("ChargeRequest missing");
        var req = Activator.CreateInstance(reqType)!;
        reqType.GetProperty("Amount")!.SetValue(req, 10L);
        reqType.GetProperty("Currency")!.SetValue(req, "USD");
        var pay = printed.GetType().GetMethod("Pay")
            ?? throw new InvalidOperationException("Pay missing");
        try {
            var result = pay.Invoke(printed, [req])!;
            var ok = (bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!;
            await Assert.That(ok).IsFalse();
        }
        catch (TargetInvocationException ex) {
            await Assert.That(ex.InnerException is not null).IsTrue();
        }
    }
}
