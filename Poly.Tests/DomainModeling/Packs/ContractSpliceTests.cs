using Poly.Ast.Nodes;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

using static Poly.Tests.TestHelpers.ContractContributions;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// #421: Lower splices a contributed endpoint body at its bind, erases a surface-only
/// sibling bind, and fails closed for an external endpoint with no body. No
/// <c>{Contract}Adapters</c> type is emitted.
/// </summary>
public sealed class ContractSpliceTests {
    private const string SiblingDsl = """
        domain Shop
        uses billing

        Order: entity {
          Open: stage {
            Pay: action (request: ChargeRequest) { transition to Done }
          }
          Done: stage { }
        }

        ChargeOrder: bind Billing Charge to Pay request
        """;

    private const string ExternalDsl = """
        domain Shop
        uses stripe

        Order: entity {
          Open: stage {
            Pay: action (request: ChargeRequest) { transition to Done }
          }
          Done: stage { }
        }

        ChargeOrder: bind Stripe Charge to Pay request
        """;

    private static ContractLibrary Billing() =>
        new("billing", BillingSurface());

    private static ContractLibrary Stripe(
        ContractSourceKind kind = ContractSourceKind.ExternalProvider,
        IReadOnlyDictionary<string, Func<Block>>? bodies = null) =>
        new(
            "stripe",
            BillingSurface(kind, name: "Stripe", source: "stripe"),
            bodies ?? new Dictionary<string, Func<Block>>(StringComparer.Ordinal) {
                ["Charge"] = TraceBody("Stripe.Charge"),
            });

    private static string Generated(string poly, ContractLibrary library, out DomainSession session) {
        var (domain, evolvedSession, analysis) = Evolve(poly, library);
        session = evolvedSession;
        session.Lower(domain, analysis);
        return new CSharpGenerator().Generate(session.Lower(domain, analysis));
    }

    [Test]
    public async Task Bind_SurfaceOnlySibling_ErasesAdapterAndRunsLocalAction() {
        var (domain, session, analysis) = Evolve(SiblingDsl, Billing());

        var module = session.Lower(domain, analysis);
        var cs = new CSharpGenerator().Generate(module);

        await Assert.That(cs.Contains("BillingAdapters", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cs.Contains("adapterResult", StringComparison.Ordinal)).IsFalse();
        await Assert.That(module.Any(t => t.Name == "ChargeRequest")).IsTrue();

        var exec = ModuleExec.For(domain);
        var order = exec.Create("Order", new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(order, "Pay", new Dictionary<string, object?> {
            ["request"] = new Dictionary<string, object?> { ["Amount"] = 10L },
        });

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(order)).IsEqualTo("Done");
    }

    [Test]
    public async Task Bind_ContributedBody_SplicesAtTheBind() {
        var cs = Generated(ExternalDsl, Stripe(), out _);

        await Assert.That(cs.Contains("StripeAdapters", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cs.Contains("Contract endpoint 'Stripe.Charge' request:", StringComparison.Ordinal)).IsTrue();
        await Assert.That(cs.Contains("if (!adapterResult.IsSuccess)", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Bind_ExternalWithoutBody_FailsClosed() {
        const string poly = """
            domain Shop

            Order: entity {
              Pay: action (request: ChargeRequest) { }
            }

            Stripe: contract external stripe v1 {
              ChargeRequest: value { Amount: Number }
              Charge: outbound operation ChargeRequest
            }

            ChargeOrder: bind Stripe Charge to Pay request
            """;
        var (domain, session, analysis) = Evolve(poly);

        var ex = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        await Assert.That(ex.Message.Contains("Stripe", StringComparison.Ordinal)).IsTrue();
        await Assert.That(ex.Message.Contains("Charge", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Bind_BodyWithoutFinalReturn_FailsClosed() {
        var bodies = new Dictionary<string, Func<Block>>(StringComparer.Ordinal) {
            ["Charge"] = () => new Block([
                new Invoke(new Member(new NamedTypeReference("DomainResult"), "Success"), []),
            ]),
        };
        var (domain, session, analysis) = Evolve(ExternalDsl, Stripe(bodies: bodies));

        var ex = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        await Assert.That(ex.Message.Contains("must end in", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Bind_BodyWithEarlyReturn_FailsClosed() {
        var bodies = new Dictionary<string, Func<Block>>(StringComparer.Ordinal) {
            ["Charge"] = () => new Block([
                new IfStatement(
                    new Constant(true),
                    new Block([new Return(new Constant(1))])),
                new Return(new Invoke(new Member(new NamedTypeReference("DomainResult"), "Success"), [])),
            ]),
        };
        var (domain, session, analysis) = Evolve(ExternalDsl, Stripe(bodies: bodies));

        var ex = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        await Assert.That(ex.Message.Contains("returns before", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Bind_BodyLocals_AreSplicedIntoTheMethod() {
        var bodies = new Dictionary<string, Func<Block>>(StringComparer.Ordinal) {
            ["Charge"] = () => new Block(
                [
                    new Assignment(
                        new Variable("amount"),
                        new Member(new Parameter("request"), "Amount")),
                    new Return(new Invoke(new Member(new NamedTypeReference("DomainResult"), "Success"), [])),
                ],
                [new Variable("amount")]),
        };
        var (domain, session, analysis) = Evolve(ExternalDsl, Stripe(bodies: bodies));

        var module = session.Lower(domain, analysis);
        var cs = new CSharpGenerator().Generate(module);

        await Assert.That(cs.Contains("amount", StringComparison.Ordinal)).IsTrue();
        await Assert.That(cs.Contains("StripeAdapters", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Bind_BodyFactoryReusedAcrossTwoBinds_FailsClosed() {
        var shared = new Block([
            new Return(new Invoke(new Member(new NamedTypeReference("DomainResult"), "Success"), [])),
        ]);
        var bodies = new Dictionary<string, Func<Block>>(StringComparer.Ordinal) {
            ["Charge"] = () => shared,
        };
        const string poly = """
            domain Shop
            uses stripe

            Order: entity {
              Pay: action (request: ChargeRequest) { }
              Refund: action (request: ChargeRequest) { }
            }

            ChargeOrder: bind Stripe Charge to Pay request
            RefundOrder: bind Stripe Charge to Refund request
            """;
        var (domain, session, analysis) = Evolve(poly, Stripe(bodies: bodies));

        var ex = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        await Assert.That(ex.Message.Contains("fresh", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    public async Task Bind_TwoActionsSameEndpoint_SpliceAndExecute() {
        const string poly = """
            domain Shop
            uses stripe

            Order: entity {
              Pay: action (request: ChargeRequest) { }
              Refund: action (request: ChargeRequest) { }
            }

            ChargeOrder: bind Stripe Charge to Pay request
            RefundOrder: bind Stripe Charge to Refund request
            """;
        var (domain, session, analysis) = Evolve(poly, Stripe());

        var files = session.Emit(domain, analysis);
        var order = files.Single(f => f.FileName == "Order.cs").Source;
        await Assert.That(order.Split("Contract endpoint 'Stripe.Charge' request:").Length - 1).IsEqualTo(2);
        await Assert.That(order.Contains("StripeAdapters", StringComparison.Ordinal)).IsFalse();

        var exec = ModuleExec.For(domain);
        var instance = exec.Create("Order", new Dictionary<string, object?>()).Instance!;
        var request = new Dictionary<string, object?> { ["Amount"] = 10L };
        await Assert.That(exec.Invoke(instance, "Pay", new Dictionary<string, object?> { ["request"] = request }).Succeeded).IsTrue();
        await Assert.That(exec.Invoke(instance, "Refund", new Dictionary<string, object?> { ["request"] = request }).Succeeded).IsTrue();
    }

    [Test]
    public async Task Bind_KeepsAclValueType() {
        var (domain, session, analysis) = Evolve(SiblingDsl, Billing());
        var module = session.Lower(domain, analysis);

        await Assert.That(module.Any(t => t.Name == "ChargeRequest")).IsTrue();
    }

    [Test]
    public async Task Bind_BodyLocalNamedLikeParameter_FailsClosed() {
        var bodies = new Dictionary<string, Func<Block>>(StringComparer.Ordinal) {
            ["Charge"] = () => new Block(
                [new Return(new Invoke(new Member(new NamedTypeReference("DomainResult"), "Success"), []))],
                [new Variable("request")]),
        };
        var (domain, session, analysis) = Evolve(ExternalDsl, Stripe(bodies: bodies));

        var ex = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        await Assert.That(ex.Message.Contains("collides", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Bind_BodyFinalExpressionNotDomainResult_FailsEmit() {
        var bodies = new Dictionary<string, Func<Block>>(StringComparer.Ordinal) {
            ["Charge"] = () => new Block([new Return(new Constant(1))]),
        };
        var (domain, session, analysis) = Evolve(ExternalDsl, Stripe(bodies: bodies));

        Assert.Throws<InvalidOperationException>(() => session.Emit(domain, analysis));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Bind_BodyReadsRequestButLocalParameterHasAnotherName_FailsClosed() {
        const string poly = """
            domain Shop
            uses stripe

            Order: entity {
              Pay: action (payload: ChargeRequest) { }
            }

            ChargeOrder: bind Stripe Charge to Pay payload
            """;
        var (domain, session, analysis) = Evolve(poly, Stripe());

        var ex = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        await Assert.That(ex.Message.Contains("must name its payload parameter 'request'", StringComparison.Ordinal)).IsTrue();
    }

}