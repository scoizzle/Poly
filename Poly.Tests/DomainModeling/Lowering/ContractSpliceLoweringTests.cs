using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

using static Poly.Tests.TestHelpers.ContractContributions;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #421: a bind to a library endpoint with a contributed body splices that body into the
/// bound action — the trace and the success come from the library, not an emitter arm.
/// The same domain must agree between the VM module and the printed C#.
/// </summary>
public sealed class ContractSpliceLoweringTests {
    private const string VoidDsl = """
        domain Shop
        uses stripe

        Order: entity {
          Total: Number default(0)
          Pay: action (request: ChargeRequest) {
            assign Total to Total
          }
        }

        ChargeOrder: bind Stripe Charge to Pay request
        """;

    private const string TypedDsl = """
        domain Shop
        uses stripe

        Order: entity {
          lines: many Line
          Pay: action (request: ChargeRequest) -> Line {
            create in lines { }
          }
        }
        Line: entity {
          Draft: stage {}
        }

        ChargeOrder: bind Stripe Charge to Pay request
        """;

    [Test]
    public async Task ContributedBody_VoidAction_SplicesOnSimulateAndPrintedCsharp() {
        var (domain, session, analysis) = Evolve(VoidDsl, StripeLibrary());
        var module = session.Lower(domain, analysis);

        var exec = ModuleExec.For(domain);
        var order = exec.Create("Order", new Dictionary<string, object?>()).Instance!;
        var request = new Dictionary<string, object?> { ["Amount"] = 10L };
        var sim = exec.Invoke(order, "Pay", new Dictionary<string, object?> { ["request"] = request });
        await Assert.That(sim.Succeeded).IsTrue();

        var cs = new CSharpGenerator().Generate(module);
        await Assert.That(cs).Contains("System.Diagnostics.Debug.WriteLine");
        await Assert.That(cs.Contains("StripeAdapters", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cs.Contains("has no in-process adapter", StringComparison.Ordinal)).IsFalse();
        var asm = ExportedCSharp.CompileAndLoad(cs);
        var printed = ExportedCSharp.CreateEntity(asm, "Order");
        var reqType = asm.GetType("ChargeRequest")
            ?? throw new InvalidOperationException("ChargeRequest missing");
        var req = Activator.CreateInstance(reqType)!;
        reqType.GetProperty("Amount", PrintedMembers.Any)!.SetValue(req, 10L);
        var pay = printed.GetType().GetMethod("Pay", PrintedMembers.Any)
            ?? throw new InvalidOperationException("Pay missing");
        var printedResult = pay.Invoke(printed, [req])
            ?? throw new InvalidOperationException("Pay returned null");
        await Assert.That((bool)printedResult.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(printedResult)!).IsTrue();
    }

    [Test]
    public async Task ContributedBody_TypedAction_KeepsTheTypedFailureGuard() {
        var (domain, session, analysis) = Evolve(TypedDsl, StripeLibrary());
        var module = session.Lower(domain, analysis);

        var exec = ModuleExec.For(domain);
        var order = exec.Create("Order", new Dictionary<string, object?>()).Instance!;
        var request = new Dictionary<string, object?> { ["Amount"] = 10L };
        var sim = exec.Invoke(order, "Pay", new Dictionary<string, object?> { ["request"] = request });
        await Assert.That(sim.Succeeded).IsTrue();

        var cs = new CSharpGenerator().Generate(module);
        await Assert.That(cs).Contains("System.Diagnostics.Debug.WriteLine");
        await Assert.That(cs).Contains(
            "return DomainResult<Line>.Failure(adapterResult.Error!.At(\"Order.Pay\"))");
        await Assert.That(cs.Contains("StripeAdapters", StringComparison.Ordinal)).IsFalse();

        var asm = ExportedCSharp.CompileAndLoad(cs);
        var printed = ExportedCSharp.CreateEntity(asm, "Order");
        var reqType = asm.GetType("ChargeRequest")
            ?? throw new InvalidOperationException("ChargeRequest missing");
        var req = Activator.CreateInstance(reqType)!;
        reqType.GetProperty("Amount", PrintedMembers.Any)!.SetValue(req, 10L);
        var pay = printed.GetType().GetMethod("Pay", PrintedMembers.Any)
            ?? throw new InvalidOperationException("Pay missing");
        var printedResult = pay.Invoke(printed, [req])
            ?? throw new InvalidOperationException("Pay returned null");
        await Assert.That((bool)printedResult.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(printedResult)!).IsTrue();
    }
    [Test]
    public async Task ContributedBody_FailureExpression_AgreesOnSimulateAndPrintedCsharp() {
        var bodies = new Dictionary<string, Func<Poly.Ast.Nodes.Block>>(StringComparer.Ordinal) {
            ["Charge"] = () => new Poly.Ast.Nodes.Block([
                new Poly.Ast.Nodes.Return(
                    new Poly.Ast.Nodes.Invoke(
                        new Poly.Ast.Nodes.Member(
                            new Poly.Ast.Nodes.NamedTypeReference("DomainResult"), "Failure"),
                        new Poly.Ast.Nodes.Constant("declined"))),
            ]),
        };
        var library = new ContractLibrary(
            "stripe",
            BillingSurface(Poly.DomainModeling.Ontology.Contract.ContractSourceKind.ExternalProvider,
                name: "Stripe", source: "stripe"),
            bodies);
        var (domain, session, analysis) = Evolve(VoidDsl, library);
        var module = session.Lower(domain, analysis);

        var exec = ModuleExec.For(domain);
        var order = exec.Create("Order", new Dictionary<string, object?>()).Instance!;
        var sim = exec.Invoke(order, "Pay", new Dictionary<string, object?> {
            ["request"] = new Dictionary<string, object?> { ["Amount"] = 10L },
        });
        await Assert.That(sim.Succeeded).IsFalse();
        await Assert.That(sim.Error).IsEqualTo("declined");
        await Assert.That(sim.Failure!.Kind).IsEqualTo(Poly.Runtime.DomainFailureKind.Constraint);
        await Assert.That(sim.Failure.ElementPath).IsEqualTo("Order.Pay");
        await Assert.That(sim.Failure.Message).IsEqualTo("declined");
        await Assert.That(sim.Failure.Guard).IsNull();

        var cs = new CSharpGenerator().Generate(module);
        var asm = ExportedCSharp.CompileAndLoad(cs);
        var printed = ExportedCSharp.CreateEntity(asm, "Order");
        var reqType = asm.GetType("ChargeRequest")
            ?? throw new InvalidOperationException("ChargeRequest missing");
        var req = Activator.CreateInstance(reqType)!;
        reqType.GetProperty("Amount", PrintedMembers.Any)!.SetValue(req, 10L);
        var pay = printed.GetType().GetMethod("Pay", PrintedMembers.Any)
            ?? throw new InvalidOperationException("Pay missing");
        var printedResult = pay.Invoke(printed, [req])
            ?? throw new InvalidOperationException("Pay returned null");
        await Assert.That((bool)printedResult.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(printedResult)!).IsFalse();
        await Assert.That((string?)printedResult.GetType().GetProperty("ErrorMessage", PrintedMembers.Any)!.GetValue(printedResult))
            .IsEqualTo("declined");
        var printedFailure = (Poly.Runtime.DomainFailure)printedResult.GetType().GetProperty("Error", PrintedMembers.Any)!.GetValue(printedResult)!;
        await Assert.That(printedFailure.Kind).IsEqualTo(Poly.Runtime.DomainFailureKind.Constraint);
        await Assert.That(printedFailure.ElementPath).IsEqualTo("Order.Pay");
        await Assert.That(printedFailure.Message).IsEqualTo("declined");
        await Assert.That(printedFailure.Guard).IsNull();
    }

}