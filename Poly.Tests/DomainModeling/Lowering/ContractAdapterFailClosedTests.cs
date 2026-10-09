using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class ContractAdapterFailClosedTests {
    [Test]
    public async Task UnboundContractAdapter_FailsClosed_OnSimulateAndPrintedCsharp() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
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
        var printedResult = pay.Invoke(printed, [req])
            ?? throw new InvalidOperationException("Pay returned null");
        var printedType = printedResult.GetType();
        await Assert.That((bool)printedType.GetProperty("IsSuccess")!.GetValue(printedResult)!).IsFalse();
        var printedMessage = printedType.GetProperty("ErrorMessage")!.GetValue(printedResult) as string;
        await Assert.That(printedMessage).IsEqualTo(sim.ErrorMessage);
        await Assert.That(printedMessage).IsEqualTo(
            "Contract endpoint 'Stripe.Charge' has no in-process adapter.");
    }

    [Test]
    public async Task UnboundContractAdapter_TypedAction_FailsClosed_OnSimulateAndPrintedCsharp() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            Order: entity {
              lines: many Line
              Pay: action (request: ChargeRequest) -> Line {
                create in lines { }
              }
            }
            Line: entity {
              Draft: stage {}
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
        await Assert.That(sim.ErrorMessage).IsEqualTo(
            "Contract endpoint 'Stripe.Charge' has no in-process adapter.");

        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(cs).Contains(
            "return DomainResult<Line>.Failure(adapterResult.ErrorMessage ?? \"\")");

        var asm = ExportedCSharp.CompileAndLoad(cs);
        var printed = ExportedCSharp.CreateEntity(asm, "Order");
        var reqType = asm.GetType("ChargeRequest")
            ?? throw new InvalidOperationException("ChargeRequest missing");
        var req = Activator.CreateInstance(reqType)!;
        reqType.GetProperty("Amount")!.SetValue(req, 10L);
        reqType.GetProperty("Currency")!.SetValue(req, "USD");
        var pay = printed.GetType().GetMethod("Pay")
            ?? throw new InvalidOperationException("Pay missing");
        var printedResult = pay.Invoke(printed, [req])
            ?? throw new InvalidOperationException("Pay returned null");
        var printedType = printedResult.GetType();
        await Assert.That((bool)printedType.GetProperty("IsSuccess")!.GetValue(printedResult)!).IsFalse();
        var printedMessage = printedType.GetProperty("ErrorMessage")!.GetValue(printedResult) as string;
        await Assert.That(printedMessage).IsEqualTo(sim.ErrorMessage);
    }
}
