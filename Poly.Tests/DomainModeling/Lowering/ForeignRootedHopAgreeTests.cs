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
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);

        var store = new DomainInstanceStore();
        var product = DomainEntityInstance.Create(E("Product"),
            new Dictionary<string, object?> { ["Stock"] = 10L }, domain);
        var item = DomainEntityInstance.Create(E("Item"),
            new Dictionary<string, object?> { ["Qty"] = 1L }, domain);
        var order = DomainEntityInstance.Create(E("Order"), domain: domain);
        store.Add(product);
        store.Add(item);
        store.Add(order);
        store.Link("product", item, product);
        store.Link("items", order, item);
        var sim = order.EvaluatePolicy(E("Order").Policies.First(p => p.Name == "HasShortItem"));

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var printedProduct = ExportedCSharp.CreateEntity(asm, "Product", ("stock", 10L));
        var printedItem = ExportedCSharp.CreateEntity(asm, "Item", ("qty", 1L));
        printedItem.GetType().GetProperty("Product")!.SetValue(printedItem, printedProduct);
        var printedOrder = ExportedCSharp.CreateEntity(asm, "Order");
        printedOrder.GetType().GetMethod("AttachItems", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(printedOrder, [printedItem]);
        var print = (bool)printedOrder.GetType().GetMethod("HasShortItem")!.Invoke(printedOrder, null)!;

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
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);

        var store = new DomainInstanceStore();
        var order = DomainEntityInstance.Create(E("Order"),
            new Dictionary<string, object?> { ["Total"] = 10L }, domain);
        var customer = DomainEntityInstance.Create(E("Customer"),
            new Dictionary<string, object?> { ["Balance"] = 5L }, domain);
        store.Add(order);
        store.Add(customer);
        store.Link("orders", customer, order);
        await Assert.That(order.InvokeAction("Activate").Succeeded).IsTrue();
        await Assert.That(customer.GetProperty<bool>("Flag")).IsTrue();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var printedOrder = ExportedCSharp.CreateEntity(asm, "Order", ("total", 10L));
        var printedCustomer = ExportedCSharp.CreateEntity(asm, "Customer", ("balance", 5L));
        printedCustomer.GetType().GetMethod("AttachOrders", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(printedCustomer, [printedOrder]);
        var activate = printedOrder.GetType().GetMethod("Activate")!.Invoke(printedOrder, null)!;
        await Assert.That((bool)activate.GetType().GetProperty("IsSuccess")!.GetValue(activate)!).IsTrue();
        await Assert.That((bool)printedCustomer.GetType().GetProperty("Flag")!.GetValue(printedCustomer)!).IsTrue();
    }
}
