using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// D2: a grandchild whose parent is a non-root aggregate member is not an orphan.
/// A chain of outbound singular navigations is one owned subgraph, not an orphan.
/// </summary>
public class OwnershipAggregateAnalysisTests {
    private static (Domain Domain, AnalysisResult Analysis) Analyze(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(dsl).Parse());
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        var domain = result.Root!;
        return (domain, DomainModelAnalyzer.Analyze(domain));
    }

    private static bool Orphan(AnalysisResult analysis, string entityName) =>
        analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.AggregateOrphan
            && d.Message.Contains($"'{entityName}'", StringComparison.Ordinal));

    [Test]
    public async Task Grandchild_NonRootParent_NoOrphanWarning() {
        var (domain, analysis) = Analyze("""
            domain Test
            Customer: entity {
              Name: Text
              orders: many Order
            }
            Order: entity {
              Total: Number
              customer: Customer
              items: many OrderItem
            }
            OrderItem: entity {
              Sku: Text
              order: Order
            }
            """);

        await Assert.That(Orphan(analysis, "OrderItem")).IsFalse();
        await Assert.That(Orphan(analysis, "Order")).IsFalse();
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate;
        var item = aggregate.Entities.First(e => e.Name == "OrderItem");
        await Assert.That(item.IsRoot).IsFalse();
        await Assert.That(item.AggregateParentName).IsEqualTo("Order");
        await Assert.That(item.ParentRelationshipName).IsEqualTo("items");
    }

    [Test]
    public async Task Grandchild_DeclaredBeforeParent_NoOrphanWarning() {
        var (domain, analysis) = Analyze("""
            domain Test
            OrderItem: entity {
              Sku: Text
              order: Order
            }
            Order: entity {
              Total: Number
              customer: Customer
              items: many OrderItem
            }
            Customer: entity {
              Name: Text
              orders: many Order
            }
            """);

        await Assert.That(Orphan(analysis, "OrderItem")).IsFalse();
        await Assert.That(Orphan(analysis, "Order")).IsFalse();
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate;
        var item = aggregate.Entities.First(e => e.Name == "OrderItem");
        await Assert.That(item.AggregateParentName).IsEqualTo("Order");
        await Assert.That(item.ParentRelationshipName).IsEqualTo("items");
    }

    [Test]
    public async Task OwnedTarget_IsNotRoot_OwnerStaysRoot() {
        // S17/S18: `address: owned Address` is composition — Address is a child of
        // Customer, and Customer must stay a root (it was mis-parented off the
        // reverse edge and lost its HTTP endpoints).
        var (domain, analysis) = Analyze("""
            domain Test
            Customer: entity {
              Name: Text
              address: owned Address
              orders: many Order
            }
            Address: entity {
              Line1: Text
            }
            Order: entity {
              Total: Number
              customer: Customer
            }
            """);

        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate;
        var customer = aggregate.Entities.First(e => e.Name == "Customer");
        var address = aggregate.Entities.First(e => e.Name == "Address");
        var order = aggregate.Entities.First(e => e.Name == "Order");

        await Assert.That(customer.IsRoot).IsTrue();
        await Assert.That(address.IsRoot).IsFalse();
        await Assert.That(address.AggregateParentName).IsEqualTo("Customer");
        await Assert.That(order.AggregateParentName).IsEqualTo("Customer");
        await Assert.That(Orphan(analysis, "Customer")).IsFalse();
    }

    [Test]
    public async Task SingularOutbound_NoInboundCollection_NotOrphan() {
        var (_, analysis) = Analyze("""
            domain Test
            Account: entity { Name: Text }
            Lead: entity {
              Name: Text
              account: Account
            }
            """);

        await Assert.That(Orphan(analysis, "Lead")).IsFalse();
        await Assert.That(Orphan(analysis, "Account")).IsFalse();
    }

    [Test]
    public async Task SingularChain_NoInboundCollection_NotOrphan() {
        var (_, analysis) = Analyze("""
            domain Test
            Plan: entity { Name: Text }
            Account: entity {
              Name: Text
              subscription: Subscription
            }
            Subscription: entity {
              Name: Text
              plan: Plan
            }
            """);

        await Assert.That(Orphan(analysis, "Account")).IsFalse();
        await Assert.That(Orphan(analysis, "Subscription")).IsFalse();
        await Assert.That(Orphan(analysis, "Plan")).IsFalse();
    }
}