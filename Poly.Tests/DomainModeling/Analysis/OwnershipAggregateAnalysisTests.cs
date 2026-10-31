using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Ownership aggregate truth: every resolved non-root has exactly one parent, a
/// parentless entity is a root, and the canonical back-reference pair
/// (<c>Owner.children: many Child</c> + <c>Child.owner: Owner</c>) does not build a
/// two-node parent cycle.
/// </summary>
public class OwnershipAggregateAnalysisTests {
    private static (Domain Domain, AnalysisResult Analysis) Analyze(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        var domain = result.Root!;
        return (domain, DomainModelAnalyzer.Analyze(domain));
    }

    private static AggregateModel Aggregate(AnalysisResult analysis, Domain domain) =>
        analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate;

    private static AggregateEntity Entity(AggregateModel aggregate, string name) =>
        aggregate.Entities.First(e => e.Name == name);

    /// <summary>
    /// The invariant every consumer relies on: a parentless entity is a root, so no
    /// entity can be a non-root without a parent (that state had no key and no HTTP
    /// endpoints), and following parents always terminates at a root.
    /// </summary>
    private static async Task AssertParentInvariant(AggregateModel aggregate) {
        foreach (var entity in aggregate.Entities) {
            if (!entity.IsRoot)
                await Assert.That(entity.AggregateParentName).IsNotNull();
            var cursor = entity;
            var seen = new HashSet<string>(StringComparer.Ordinal) { entity.Name };
            while (cursor?.AggregateParentName is not null) {
                await Assert.That(seen.Add(cursor.AggregateParentName)).IsTrue();
                cursor = Entity(aggregate, cursor.AggregateParentName);
            }
            await Assert.That(cursor!.IsRoot).IsTrue();
        }
    }

    [Test]
    public async Task Grandchild_ResolvesToItsParent() {
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

        var aggregate = Aggregate(analysis, domain);
        var item = Entity(aggregate, "OrderItem");
        await Assert.That(item.IsRoot).IsFalse();
        await Assert.That(item.AggregateParentName).IsEqualTo("Order");
        await Assert.That(item.ParentRelationshipName).IsEqualTo("items");
        await Assert.That(Entity(aggregate, "Customer").IsRoot).IsTrue();
        await Assert.That(Entity(aggregate, "Order").AggregateParentName).IsEqualTo("Customer");
        await AssertParentInvariant(aggregate);
    }

    [Test]
    public async Task Grandchild_DeclaredBeforeParent_Resolves() {
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

        var aggregate = Aggregate(analysis, domain);
        var item = Entity(aggregate, "OrderItem");
        await Assert.That(item.AggregateParentName).IsEqualTo("Order");
        await Assert.That(item.ParentRelationshipName).IsEqualTo("items");
        await AssertParentInvariant(aggregate);
    }

    [Test]
    public async Task BackReferencePair_NoParentCycle() {
        // S1b: Member.bookings + Booking.member is one edge declared twice. Taking the
        // inbound singular as a parent made Member.parent = Booking and
        // Booking.parent = Member — a cycle that corrupted every consumer of the
        // aggregate (facts, route nesting, keys).
        var (domain, analysis) = Analyze("""
            domain Test
            Plan: entity {
              Code: Text
            }
            Member: entity {
              Name: Text
              plan: Plan
              bookings: many Booking
            }
            Booking: entity {
              Amount: Number
              member: Member
            }
            """);

        var aggregate = Aggregate(analysis, domain);
        await Assert.That(Entity(aggregate, "Plan").IsRoot).IsTrue();
        await Assert.That(Entity(aggregate, "Plan").AggregateParentName).IsNull();
        var booking = Entity(aggregate, "Booking");
        await Assert.That(booking.IsRoot).IsFalse();
        await Assert.That(booking.AggregateParentName).IsEqualTo("Member");
        await Assert.That(booking.ParentRelationshipName).IsEqualTo("bookings");
        await Assert.That(booking.BackReferencePropertyName).IsEqualTo("member");
        // Nothing owns Member: the plain reference to Plan is not ownership (that would
        // make Member a child of the entity it references).
        await Assert.That(Entity(aggregate, "Member").AggregateParentName).IsNull();
        await AssertParentInvariant(aggregate);
    }

    [Test]
    public async Task ReferenceOnlyShape_IsARoot() {
        // A plain singular reference whose target owns nothing (and is owned by nobody)
        // leaves the referrer parentless. It is a root — not a non-root with no key and
        // no HTTP endpoints, which is what the old pass produced (silently).
        var (domain, analysis) = Analyze("""
            domain Test
            Account: entity {
              Name: Text
            }
            Lead: entity {
              Name: Text
              account: Account
            }
            """);

        var aggregate = Aggregate(analysis, domain);
        await Assert.That(Entity(aggregate, "Lead").IsRoot).IsTrue();
        await Assert.That(Entity(aggregate, "Lead").AggregateParentName).IsNull();
        await Assert.That(Entity(aggregate, "Account").IsRoot).IsTrue();
        await AssertParentInvariant(aggregate);
    }

    [Test]
    public async Task OwnedTarget_IsNotRoot_OwnerStaysRoot() {
        // S17/S18: `address: owned Address` is composition — Address is a child of
        // Customer, and Customer stays a root (it was mis-parented off the reverse edge
        // and lost its HTTP endpoints).
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

        var aggregate = Aggregate(analysis, domain);
        await Assert.That(Entity(aggregate, "Customer").IsRoot).IsTrue();
        var address = Entity(aggregate, "Address");
        await Assert.That(address.IsRoot).IsFalse();
        await Assert.That(address.AggregateParentName).IsEqualTo("Customer");
        await Assert.That(Entity(aggregate, "Order").AggregateParentName).IsEqualTo("Customer");
        await AssertParentInvariant(aggregate);
    }

    [Test]
    public async Task SingularChain_IsAcyclic() {
        // Plain singulars are references. Ownership is inferred from an inbound singular
        // (the 1:1 pair convention), so *which* of the two chain links is the child is a
        // resolution detail — what is guaranteed is that the chain cannot close a loop and
        // that a parentless entity is a root.
        var (domain, analysis) = Analyze("""
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

        var aggregate = Aggregate(analysis, domain);
        var account = Entity(aggregate, "Account");
        var subscription = Entity(aggregate, "Subscription");
        await Assert.That(Entity(aggregate, "Plan").IsRoot).IsTrue();
        await Assert.That(
            account.AggregateParentName == "Subscription" && subscription.AggregateParentName == "Account")
            .IsFalse();
        await AssertParentInvariant(aggregate);
    }

    [Test]
    public async Task TwoNodeMutualReference_ResolvesWithoutACycle() {
        // The pathological shape: each entity's only ownership signal points at the
        // other. One may take the other as its parent; they must not both.
        var (domain, analysis) = Analyze("""
            domain Test
            A: entity {
              Name: Text
              b: B
            }
            B: entity {
              Name: Text
              a: A
            }
            """);

        var aggregate = Aggregate(analysis, domain);
        var a = Entity(aggregate, "A");
        var b = Entity(aggregate, "B");
        await Assert.That(a.AggregateParentName == "B" && b.AggregateParentName == "A").IsFalse();
        await AssertParentInvariant(aggregate);
    }
}