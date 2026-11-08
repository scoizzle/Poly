using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Ontology;

using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;
using DbmsPack = Poly.DslCompiler.DbmsPack;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #421: a bind to a surface-only in-solution contract compiles away. The exporter emits
/// no <c>{Contract}Adapters</c> type and no adapter invoke; the local action still runs.
/// An external endpoint with no body is covered by ContractSpliceTests.
/// </summary>
public class ContractBindingExportTests {
    private static Compiler CompilerWithHttpHost() =>
        new Compiler().Load(new HttpLibrary());

    /// <summary>A billing domain whose public Ledger.Charge action projects to the Billing
    /// contract endpoint (pack-3b producer). The child's Ledger entity must never surface in
    /// export; only its name may appear as a name-only surface entry.</summary>
    private static Domain BillingSource() => ParseDomain("""
        domain billing

        public value ChargeRequest {
          Amount: Number
          Currency: Text
        }

        public entity Ledger {
          public action Charge (request: ChargeRequest) {
          }
        }
        """);

    /// <summary>Parent Shop base: root Order entity + a declared (empty) internal Billing contract.
    /// Parsed without the bind because the parent's action references the contract value type, which
    /// only exists after the producer fills the contract from the loaded billing domain.</summary>
    private static Domain BaseParent() => ParseDomain("""
        domain Shop
        Order: entity {
          Number: Text unique
        }
        Billing: contract internal billing v1 {}
        """);

    private static Domain ParseDomain(string poly) {
        var ctx = ExtensionCatalog.Core.Authoring;
        var parser = new PolyDslParser(poly, ctx);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).ApplyAndAnalyze(changes);
        if (!result.Succeeded) throw new InvalidOperationException("Domain evolution failed");
        return result.Root!;
    }

    /// <summary>Parent domain after the billing contract is filled and the root Order.Pay action is
    /// bound to Billing.Charge. Pay has no local effects; the surface-only sibling bind erases, so
    /// the emitted action is the empty local implementation.</summary>
    private static Domain FilledParent() {
        var baseParent = BaseParent();
        var filled = new DomainSuite([BillingSource(), baseParent])
            .FillInternalContracts(baseParent);
        var result = new DomainEvolution(filled).Evolve()
            .AddActionWithParameters("Order", "Pay",
                new Property("request", new DomainTypeReference("ChargeRequest"), []))
            .AddContractBinding("ChargeOrder", "Billing", "Charge", "Pay", "request")
            .Apply();
        if (!result.Succeeded) throw new InvalidOperationException("Domain evolution failed");
        return result.Root!;
    }

    [Test]
    public async Task BoundAction_Export_ErasesSiblingBind() {
        var poly = new DomainDslPrinter().Print(FilledParent());
        var result = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);

        await Assert.That(result.Success).IsTrue();
        var files = result.Files!;

        // A surface-only sibling bind is compiled away: no adapter, no invoke. The
        // local action remains, and the contract value type stays in the module.
        var order = files.Single(f => f.FileName == "Order.cs").Source;
        await Assert.That(order.Contains("BillingAdapters", StringComparison.Ordinal)).IsFalse();
        await Assert.That(order.Contains("adapterResult", StringComparison.Ordinal)).IsFalse();
        await Assert.That(order).Contains("public DomainResult Pay(ChargeRequest request)");

        var types = files.Single(f => f.FileName == "Poly.Types.cs").Source;
        await Assert.That(types.Contains("class BillingAdapters", StringComparison.Ordinal)).IsFalse();
        await Assert.That(types).Contains("ChargeRequest");

        // Child Ledger entity never becomes a public route.
        var program = files.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(program.Contains("Ledger")).IsFalse();
    }

    [Test]
    public async Task BoundAction_Export_TwoBindingsSameEndpoint_EraseWithoutSharedType() {
        // Two bindings may target the same contract endpoint. With a surface-only sibling
        // contribution both erase, and no shared adapter member is emitted for either.
        var baseParent = BaseParent();
        var filled = new DomainSuite([BillingSource(), baseParent]).FillInternalContracts(baseParent);
        var result = new DomainEvolution(filled).Evolve()
            .AddActionWithParameters("Order", "Pay",
                new Property("request", new DomainTypeReference("ChargeRequest"), []))
            .AddActionWithParameters("Order", "Refund",
                new Property("request", new DomainTypeReference("ChargeRequest"), []))
            .AddContractBinding("ChargeOrder", "Billing", "Charge", "Pay", "request")
            .AddContractBinding("RefundCharge", "Billing", "Charge", "Refund", "request")
            .Apply();
        if (!result.Succeeded) throw new InvalidOperationException("Domain evolution failed");

        var poly = new DomainDslPrinter().Print(result.Root!);
        var compiled = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);
        await Assert.That(compiled.Success).IsTrue();

        var types = compiled.Files!.Single(f => f.FileName == "Poly.Types.cs").Source;
        await Assert.That(types.Contains("BillingAdapters", StringComparison.Ordinal)).IsFalse();

        var order = compiled.Files!.Single(f => f.FileName == "Order.cs").Source;
        await Assert.That(order).Contains("public DomainResult Pay(ChargeRequest request)");
        await Assert.That(order).Contains("public DomainResult Refund(ChargeRequest request)");
    }

    [Test]
    public async Task BoundAction_Export_ErasedBindLeavesTheLocalActionMethod() {
        var poly = new DomainDslPrinter().Print(FilledParent());
        var result = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);

        await Assert.That(result.Success).IsTrue();
        var order = result.Files!.Single(f => f.FileName == "Order.cs").Source;

        // Erase removes the call, not the action: the method is still emitted and
        // contains no adapter expression.
        await Assert.That(order).Contains("public DomainResult Pay(ChargeRequest request)");
        await Assert.That(order.Contains("BillingAdapters", StringComparison.Ordinal)).IsFalse();
    }
}