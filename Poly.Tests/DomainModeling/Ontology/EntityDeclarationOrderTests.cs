using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;

using Entity = Poly.DomainModeling.Ontology.Entity;

namespace Poly.Tests.DomainModeling.Ontology;

/// <summary>
/// D-01 (#688, Q12): the one entity-member order reader. The merge over
/// <see cref="Entity.MemberOrder"/> is monotone, so evolution never reorders what was placed.
/// </summary>
public sealed class EntityDeclarationOrderTests {
    private const string Interleaved = """
        domain Seam
        Order: entity {
          P1: Text
          links: many Line
          P2: Text
          S1: stage { }
          A1: action { }
          Pol: policy { P1 == "x" }
        }
        Line: entity { Sku: Text }
        """;

    private static Domain Parse(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(dsl).Parse());
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return result.Root;
    }

    private static Domain Evolve(Domain domain, params DomainChange[] changes) {
        var result = new DomainEvolution(domain).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return result.Root;
    }

    private static Entity Order(Domain domain) =>
        domain.Types.OfType<Entity>().Single(e => e.Name == "Order");

    private static string NameOf(Node node) => node switch {
        Property p => p.Name,
        Relationship r => r.Name,
        Stage s => s.Name,
        Poly.DomainModeling.Ontology.Action a => a.Name,
        Policy p => p.Name,
        _ => throw new InvalidOperationException(node.GetType().Name),
    };

    private static string Seq(Entity entity) =>
        string.Join(", ", EntityDeclarationOrder.Members(entity).Select(m => $"{m.Kind}:{NameOf(m.Node)}"));

    [Test]
    public async Task Members_CompleteOrder_IsInterleaved() {
        await Assert.That(Seq(Order(Parse(Interleaved))))
            .IsEqualTo("Property:P1, Navigation:links, Property:P2, Stage:S1, Action:A1, Policy:Pol");
    }

    [Test]
    public async Task Members_EmptyMemberOrder_FallsBackToPerListOrder() {
        var entity = Order(Parse(Interleaved)) with { MemberOrder = [] };

        await Assert.That(Seq(entity))
            .IsEqualTo("Property:P1, Property:P2, Navigation:links, Stage:S1, Action:A1, Policy:Pol");
    }

    [Test]
    public async Task Members_AfterAddProperty_KeepsExistingOrderAndAppends() {
        var domain = Evolve(Parse(Interleaved),
            new AddPropertyToEntityChange("Order", new Property("P3", new DomainTypeReference("Text"), [])));

        await Assert.That(Seq(Order(domain)))
            .IsEqualTo("Property:P1, Navigation:links, Property:P2, Stage:S1, Action:A1, Policy:Pol, Property:P3");
    }

    [Test]
    public async Task Members_AfterRemoveProperty_DropsOnlyThatMember() {
        var domain = Evolve(Parse(Interleaved), new RemovePropertyFromEntityChange("Order", "P2"));

        await Assert.That(Seq(Order(domain)))
            .IsEqualTo("Property:P1, Navigation:links, Stage:S1, Action:A1, Policy:Pol");
    }

    [Test]
    public async Task Members_StaleEntry_IsDroppedAndTheMemberAppended() {
        var domain = Evolve(Parse(Interleaved), new SetEntityMemberOrderChange("Order", [
            new EntityMemberRef(EntityMemberKind.Property, "Gone"),
            new EntityMemberRef(EntityMemberKind.Property, "links"),
            new EntityMemberRef(EntityMemberKind.Property, "P2"),
        ]));

        await Assert.That(Seq(Order(domain)))
            .IsEqualTo("Property:P2, Property:P1, Navigation:links, Stage:S1, Action:A1, Policy:Pol");
    }

    [Test]
    public async Task Members_DuplicatePair_FirstMatchWins() {
        var domain = Evolve(Parse(Interleaved), new SetEntityMemberOrderChange("Order", [
            new EntityMemberRef(EntityMemberKind.Property, "P2"),
            new EntityMemberRef(EntityMemberKind.Property, "P1"),
            new EntityMemberRef(EntityMemberKind.Property, "P2"),
        ]));

        await Assert.That(Seq(Order(domain)))
            .IsEqualTo("Property:P2, Property:P1, Navigation:links, Stage:S1, Action:A1, Policy:Pol");
    }
}