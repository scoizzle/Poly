using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// S-15 (#681, Q12): an entity records the order in which its members were authored —
/// properties, navigations, stages, entity-level actions and entity-level policies,
/// interleaved exactly as written — and <see cref="Domain.Types"/> is already document
/// order. Provider-only: no consumer reads <c>MemberOrder</c> until G-18, D-01, L-07,
/// C-10 and L-08.
/// </summary>
public sealed class DeclarationOrderTests {
    /// <summary>Property, stage, property, action, policy, navigation, interleaved.</summary>
    private const string InterleavedDsl = """
        domain Orders
        Customer: entity {
          Name: Text
        }
        Order: entity {
          Id: Text
          Status: stage { }
          Total: Number
          Submit: action { }
          Valid: policy { Total > 0 }
          customer: Customer
        }
        """;

    /// <summary>
    /// Every entity-body form the resolver creates a member from, in one entity, plus the
    /// two shapes that are <em>not</em> entity members: an entity-level subscription and a
    /// stage-scoped action. The stage/property pair named <c>Dup</c> pins that a name alone
    /// cannot place a member (K8).
    /// </summary>
    private const string CoverageDsl = """
        domain All
        uses temporal
        Status: enum { Active, Closed }
        LineItem: entity {
          Sku: Text
        }
        Customer: entity {
          Name: Text
        }
        Part: entity {
          Code: Text
        }
        Thing: entity {
          Total: Number
          Q: Text
          Status: Status
          Owner: Customer
          Lines: many LineItem
          Main: one Customer
          Parts: owned Part
          Number: Text
          Text: Date
          Dup: Text
          Open: stage {
            Advance: action { }
          }
          Ship: action { }
          public action Pay { }
          Cancel(reason: Text): action { }
          Ok: policy { Total > 0 }
          Retry: { }
          when Owner Active { }
          Dup: stage { }
        }
        """;

    private static Domain Parse(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(dsl).Parse());
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return result.Root;
    }

    private static Entity Entity(Domain domain, string name) =>
        domain.Types.OfType<Entity>().Single(e => e.Name == name);

    private static string Order(IEnumerable<EntityMemberRef> members) =>
        string.Join(", ", members.Select(m => $"{m.Kind}:{m.Name}"));

    [Test]
    public async Task Entity_MemberOrder_MatchesDocument_ForInterleavedMembers() {
        var order = Entity(Parse(InterleavedDsl), "Order");

        await Assert.That(Order(order.MemberOrder))
            .IsEqualTo("Property:Id, Stage:Status, Property:Total, Action:Submit, Policy:Valid, Navigation:customer");
    }

    /// <summary>
    /// The anti-miss test: every member-creating arm records exactly one entry, the
    /// subscription and the stage-scoped action record none, and a stage and a property
    /// sharing a name survive as two entries with distinct kinds.
    /// </summary>
    [Test]
    public async Task MemberOrder_CoversEveryEntityMember_AndNothingElse() {
        var thing = Entity(Parse(CoverageDsl), "Thing");

        await Assert.That(thing.MemberOrder.Count).IsEqualTo(17);
        await Assert.That(Order(thing.MemberOrder)).IsEqualTo(
            "Property:Total, Property:Q, Property:Status, Navigation:Owner, Navigation:Lines, " +
            "Navigation:Main, Navigation:Parts, Property:Number, Property:Text, Property:Dup, " +
            "Stage:Open, Action:Ship, Action:Pay, Action:Cancel, Policy:Ok, Action:Retry, Stage:Dup");
        await Assert.That(thing.MemberOrder.Any(m => m.Name == "Advance")).IsFalse();

        var duplicates = thing.MemberOrder.Where(m => m.Name == "Dup").ToList();
        await Assert.That(duplicates.Count).IsEqualTo(2);
        await Assert.That(duplicates[0].Kind).IsEqualTo(EntityMemberKind.Property);
        await Assert.That(duplicates[1].Kind).IsEqualTo(EntityMemberKind.Stage);
    }

    [Test]
    public async Task SetMemberOrder_MissingEntity_IsAMutationError() {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply([new SetEntityMemberOrderChange("Missing", [
                new EntityMemberRef(EntityMemberKind.Property, "Q"),
            ])]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.MutationErrors.Single()).Contains("Missing");
    }

    /// <summary>
    /// After S-08 the primitive prefix is synthetic and the authored types follow in document
    /// order. The prefix's internal order differs with the <c>uses</c> line, so primitives are
    /// filtered out rather than ordered (S-08 follow-up, K6).
    /// </summary>
    [Test]
    public async Task DomainTypes_AreInDocumentOrder_NotSorted() {
        var root = Parse("""
            domain Order
            Zebra: enum { A, B }
            Mango: value { Amount: Number }
            Apple: entity { Q: Text }
            Beta: entity { R: Text }
            """);

        var authored = root.Types.Where(t => t is not PrimitiveType).Select(t => t.Name).ToList();
        await Assert.That(string.Join(", ", authored)).IsEqualTo("Zebra, Mango, Apple, Beta");
        await Assert.That(authored.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(authored.Count);
        await Assert.That(root.Types.OfType<PrimitiveType>().Count()).IsGreaterThan(0);
    }

    /// <summary>An order is a fact of authored text; a C# literal entity carries none.</summary>
    [Test]
    public async Task MemberOrder_IsEmpty_ForProgrammaticConstruction() {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply([new AddEntityChange("X", [new Property("P", new DomainTypeReference("Text"), [])])]);

        await Assert.That(Entity(result.Root, "X").MemberOrder).IsEmpty();
    }

    /// <summary>
    /// <c>MemberOrder</c> is deliberately not a <see cref="Node"/> child: adding it there would
    /// double-walk every member in every analysis pass.
    /// </summary>
    [Test]
    public async Task MemberOrder_IsNotAChild() {
        var thing = Entity(Parse(CoverageDsl), "Thing");

        var expected = thing.Properties.Count + thing.Constraints.Count + thing.Facets.Count
            + thing.Actions.Count + thing.Policies.Count + thing.Stages.Count
            + thing.Subscriptions.Count + thing.Navigations.Count;
        await Assert.That(thing.Children.Count()).IsEqualTo(expected);
        await Assert.That(thing.MemberOrder.Count).IsGreaterThan(0);
    }

    /// <summary>
    /// The analyzed root keeps the same order the resolver wrote, and the authored
    /// <see cref="Domain.Types"/> order survives analysis unchanged.
    /// </summary>
    [Test]
    public async Task MemberOrder_OnAnalyzedRoot_IsNonEmpty_AndTypesOrderUnchanged() {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser("""
                domain Shop
                Customer: entity {
                  Name: Text
                  orders: many Order
                }
                Order: entity {
                  Total: Number
                  customer: Customer
                }
                """).Parse());

        await Assert.That(result.Succeeded).IsTrue();
        var order = Entity(result.Root, "Order");
        await Assert.That(order.MemberOrder.Count).IsGreaterThan(0);
        await Assert.That(Order(order.MemberOrder)).IsEqualTo("Property:Total, Navigation:customer");

        var authored = result.Root.Types.Where(t => t is not PrimitiveType).Select(t => t.Name);
        await Assert.That(string.Join(", ", authored)).IsEqualTo("Customer, Order");
    }
}