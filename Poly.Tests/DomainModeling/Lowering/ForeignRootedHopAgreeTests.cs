using System.Reflection;

using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Value hops that are rooted on a quantifier item or a subscription binder are
/// guarded (or not) on that root, never on the entity that owns the comparison.
/// Each model is checked with and without an unrelated same-named relationship
/// on the outer entity.
/// </summary>
public class ForeignRootedHopAgreeTests {
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task QuantifierBodyHop_IsGuardedOnTheItem(bool outerHasProduct) {
        var outerProduct = outerHasProduct ? "product: Product" : "";
        var (domain, analysis) = EvolvedDomain.FromDsl($$"""
            domain Shop
            Product: entity {
              Stock: Number
            }
            Item: entity {
              Qty: Number
              product: Product
            }
            Order: entity {
              items: many Item
              {{outerProduct}}
              HasShortItem: policy { (count items where Qty < product Stock) > 0 }
            }
            """);
        var exec = ModuleExec.For(domain);
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);

        var product = exec.Create("Product", new Dictionary<string, object?> { ["Stock"] = 10L }).Instance!;
        var item = exec.Create("Item", new Dictionary<string, object?> { ["Qty"] = 1L }).Instance!;
        var order = exec.Create("Order", new Dictionary<string, object?>()).Instance!;
        exec.Link("product", item, product);
        exec.Link("items", order, item);
        var sim = exec.EvaluatePolicy(order, (E("Order").Policies.First(p => p.Name == "HasShortItem")).Name);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var printedProduct = ExportedCSharp.CreateEntity(asm, "Product", ("stock", 10L));
        var printedItem = ExportedCSharp.CreateEntity(asm, "Item", ("qty", 1L));
        printedItem.GetType().GetProperty("Product", PrintedMembers.Any)!.SetValue(printedItem, printedProduct);
        var printedOrder = ExportedCSharp.CreateEntity(asm, "Order");
        printedOrder.GetType().GetMethod("AttachItems", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(printedOrder, [printedItem]);
        var print = (bool)printedOrder.GetType().GetMethod("HasShortItem", PrintedMembers.Any)!.Invoke(printedOrder, null)!;

        await Assert.That(sim).IsTrue();
        await Assert.That(print).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SubscriptionBinderOperand_IsNotGuardedAsAnOuterHop(bool outerHasOrder) {
        var outerOrder = outerHasOrder ? "order: Order" : "";
        var (domain, analysis) = EvolvedDomain.FromDsl($$"""
            domain Shop
            Order: entity {
              Total: Number
              customer: Customer via orders
              Draft: stage {
                Activate: action { transition to Active }
              }
              Active: stage {}
            }
            Customer: entity {
              Balance: Number
              Flag: Boolean default(false)
              orders: many Order
              {{outerOrder}}
              when orders Active as order {
                if (Balance < order Total) { assign Flag to true }
              }
            }
            """);
        var exec = ModuleExec.For(domain);

        var order = exec.Create("Order", new Dictionary<string, object?> { ["Total"] = 10L }).Instance!;
        var customer = exec.Create("Customer", new Dictionary<string, object?> { ["Balance"] = 5L }).Instance!;
        exec.Link("orders", customer, order);
        await Assert.That(exec.Invoke(order, "Activate").Succeeded).IsTrue();
        await Assert.That((bool)customer.Bag["Flag"]!).IsTrue();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var printedOrder = ExportedCSharp.CreateEntity(asm, "Order", ("total", 10L));
        var printedCustomer = ExportedCSharp.CreateEntity(asm, "Customer", ("balance", 5L));
        printedCustomer.GetType().GetMethod("AttachOrders", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(printedCustomer, [printedOrder]);
        var activate = printedOrder.GetType().GetMethod("Activate", PrintedMembers.Any)!.Invoke(printedOrder, null)!;
        await Assert.That((bool)activate.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(activate)!).IsTrue();
        await Assert.That((bool)printedCustomer.GetType().GetProperty("Flag", PrintedMembers.Any)!.GetValue(printedCustomer)!).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConditionNavigationOperand_IsGuardedOnItsOwnHop(bool outerHasMentor) {
        var outerMentor = outerHasMentor ? "mentor: Mentor" : "";
        var (domain, analysis) = EvolvedDomain.FromDsl($$"""
            domain Shop
            Mentor: entity {
              Age: Number
            }
            Advisor: entity {
              Age: Number
              mentor: Mentor
            }
            Customer: entity {
              Ready: Boolean default(true)
              advisor: Advisor
              {{outerMentor}}
              ReadyMatchesAdvisor: policy { Ready is (advisor where Age < mentor Age) }
            }
            """);
        var exec = ModuleExec.For(domain);
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);

        var mentor = exec.Create("Mentor", new Dictionary<string, object?> { ["Age"] = 50L }).Instance!;
        var advisor = exec.Create("Advisor", new Dictionary<string, object?> { ["Age"] = 40L }).Instance!;
        var customer = exec.Create("Customer", new Dictionary<string, object?>()).Instance!;
        exec.Link("mentor", advisor, mentor);
        exec.Link("advisor", customer, advisor);
        var sim = exec.EvaluatePolicy(customer, (E("Customer").Policies.First(p => p.Name == "ReadyMatchesAdvisor")).Name);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var printedMentor = ExportedCSharp.CreateEntity(asm, "Mentor", ("age", 50L));
        var printedAdvisor = ExportedCSharp.CreateEntity(asm, "Advisor", ("age", 40L));
        printedAdvisor.GetType().GetProperty("Mentor", PrintedMembers.Any)!.SetValue(printedAdvisor, printedMentor);
        var printedCustomer = ExportedCSharp.CreateEntity(asm, "Customer");
        printedCustomer.GetType().GetProperty("Advisor", PrintedMembers.Any)!.SetValue(printedCustomer, printedAdvisor);
        var print = (bool)printedCustomer.GetType().GetMethod("ReadyMatchesAdvisor", PrintedMembers.Any)!.Invoke(printedCustomer, null)!;

        await Assert.That(sim).IsTrue();
        await Assert.That(print).IsTrue();
    }
}