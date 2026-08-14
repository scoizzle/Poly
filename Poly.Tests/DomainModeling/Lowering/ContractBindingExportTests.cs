using Poly.DomainModeling;
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Packs;
using Poly.DomainModeling.Parsing;
using Poly.Tests.TestHelpers;

using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;
using DbmsPack = Poly.DslCompiler.DbmsPack;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// pack-3c-3: an exported root action that <c>bind</c>s to a contract endpoint
/// invokes the adapter (a documented not-implemented adapter that throws), rather
/// than a bodyless local implementation. The binding is never dropped by export.
/// </summary>
public class ContractBindingExportTests {
    /// <summary>A billing domain whose Ledger.Charge action projects to the Billing contract
    /// endpoint (pack-3b producer). The child's Ledger entity must never surface in export.</summary>
    private static Domain BillingSource() =>
        DomainFactory.Create("billing", b => b
            .AddValueType("ChargeRequest",
                new Property("Amount", new DomainTypeReference("Number"), []),
                new Property("Currency", new DomainTypeReference("Text"), []))
            .AddEntity("Ledger")
            .AddActionWithParameters("Ledger", "Charge",
                new Property("request", new DomainTypeReference("ChargeRequest"), [])));

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
        var parser = new PolyDslParser(poly, ctx.Parser);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        if (!result.Succeeded) throw new InvalidOperationException("Domain evolution failed");
        return result.Root!;
    }

    /// <summary>Parent domain after the billing contract is filled and the root Order.Pay action is
    /// bound to Billing.Charge. Pay has no local effects — a bodyless implementation would silently
    /// succeed, which is exactly what the export must not do.</summary>
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
    public async Task BoundAction_Export_InvokesAdapterThroughBinding() {
        var poly = new DomainDslPrinter().Print(FilledParent());
        var result = new Compiler().Compile(poly, CompileMode.All, DbmsPack.Sqlite);

        await Assert.That(result.Success).IsTrue();
        var files = result.Files!;

        // The bound root action's generated method calls through the binding — the
        // adapter for Billing.Charge — instead of a bodyless local implementation.
        var order = files.Single(f => f.FileName == "Order.cs").Source;
        await Assert.That(order).Contains("BillingAdapters.Charge(request)");

        // The adapter is emitted, not dropped: a documented fail-closed stub that
        // throws NotImplementedException at runtime (no silent no-op).
        var types = files.Single(f => f.FileName == "Poly.Types.cs").Source;
        await Assert.That(types).Contains("class BillingAdapters");
        await Assert.That(types).Contains("NotImplementedException");

        // Child Ledger entity never becomes a public route.
        var program = files.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(program.Contains("Ledger")).IsFalse();
    }

    [Test]
    public async Task BoundAction_Export_TwoBindingsSameEndpoint_EmitsSingleAdapterMethod() {
        // Two bindings may target the same contract endpoint; the emitted adapter has one
        // method per endpoint (shared by every binding to it) — never a duplicate member.
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
        var compiled = new Compiler().Compile(poly, CompileMode.All, DbmsPack.Sqlite);
        await Assert.That(compiled.Success).IsTrue();

        var types = compiled.Files!.Single(f => f.FileName == "Poly.Types.cs").Source;
        await Assert.That(types.Split("public static void Charge(").Length - 1).IsEqualTo(1);

        var order = compiled.Files!.Single(f => f.FileName == "Order.cs").Source;
        await Assert.That(order).Contains("BillingAdapters.Charge(request)");
    }

    [Test]
    public async Task BoundAction_Export_MethodBodyNotSilentSuccess() {
        var poly = new DomainDslPrinter().Print(FilledParent());
        var result = new Compiler().Compile(poly, CompileMode.All, DbmsPack.Sqlite);

        await Assert.That(result.Success).IsTrue();
        var order = result.Files!.Single(f => f.FileName == "Order.cs").Source;

        // The bound action is not exported as an expression-bodied silent success —
        // the adapter invocation is a real statement in the method body.
        await Assert.That(order.Contains("Pay(ChargeRequest request) => DomainResult.Success()")).IsFalse();
        await Assert.That(order).Contains("BillingAdapters.Charge(request)");
    }
}