using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// L-22b (A4): the inverse of a navigation is resolved once, in <see cref="StorageAnalyzer"/>, with the pairing rule
/// <c>StructuralDomainAnalyzer</c> enforces: the navigation's own <c>via</c>, else the peer that declares into it,
/// else the target's only unclaimed navigation back. Consumers (the pack's EF mapping) read
/// <see cref="StorageNavigation.InversePropertyName"/>; none re-derives it.
/// </summary>
public sealed class StorageNavigationInverseTests {
    private static StorageEntity Storage(string dsl, string entity) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        return new StorageAnalyzer(result.Root, result.Analysis).Analyze().Entities.Single(e => e.Name == entity);
    }

    private static string? InverseOf(StorageEntity entity, string propertyName) =>
        entity.CollectionNavigations.Concat(entity.ReferenceNavigations).Single(n => n.PropertyName == propertyName).InversePropertyName;

    [Test]
    public async Task InversePropertyName_WhenPeerDeclaresVia_IsTheDeclaringPeer() {
        const string Dsl = """
            domain Test

            Line: entity {
              Qty: Number default(1)
              order: Order via lines
            }

            Order: entity {
              lines: many Line
            }
            """;

        await Assert.That(InverseOf(Storage(Dsl, "Order"), "Lines")).IsEqualTo("Order");
        await Assert.That(InverseOf(Storage(Dsl, "Line"), "Order")).IsEqualTo("Lines");
    }

    [Test]
    public async Task InversePropertyName_WhenOwnSideDeclaresVia_IsTheDeclaredPeer() {
        const string Dsl = """
            domain Test

            Line: entity {
              Qty: Number default(1)
              order: Order
            }

            Order: entity {
              lines: many Line via order
            }
            """;

        await Assert.That(InverseOf(Storage(Dsl, "Order"), "Lines")).IsEqualTo("Order");
        await Assert.That(InverseOf(Storage(Dsl, "Line"), "Order")).IsEqualTo("Lines");
    }

    [Test]
    public async Task InversePropertyName_WhenNeitherDeclaresAndOneNavigationBackExists_IsThatNavigation() {
        const string Dsl = """
            domain Test

            Line: entity {
              Qty: Number default(1)
              order: Order
            }

            Order: entity {
              lines: many Line
            }
            """;

        await Assert.That(InverseOf(Storage(Dsl, "Order"), "Lines")).IsEqualTo("Order");
    }

    [Test]
    public async Task InversePropertyName_WhenOneSided_IsNull() {
        const string Dsl = """
            domain Test

            Line: entity {
              Qty: Number default(1)
            }

            Order: entity {
              lines: many Line
            }
            """;

        await Assert.That(InverseOf(Storage(Dsl, "Order"), "Lines")).IsNull();
    }

    // An ambiguous undeclared pair is already an analysis error (DMREL), so the model API builds it directly.
    [Test]
    public async Task InversePropertyName_WhenTwoUnclaimedPeersPointBack_IsNull() {
        var line = new Poly.DomainModeling.Ontology.Entity("Line", [], [], [], []);
        var order = new Poly.DomainModeling.Ontology.Entity("Order", [], [], [], []);
        static Poly.DomainModeling.Ontology.Relationship Nav(string name, string from, string to, Poly.DomainModeling.Ontology.RelationshipCardinality c) =>
            new(name, new Poly.DomainModeling.Ontology.DomainTypeReference(from), new Poly.DomainModeling.Ontology.DomainTypeReference(to), c, []);
        var domain = DomainTestFactory.Create("T", [line, order], [
            Nav("lines", "Order", "Line", Poly.DomainModeling.Ontology.RelationshipCardinality.OneToMany),
            Nav("a", "Line", "Order", Poly.DomainModeling.Ontology.RelationshipCardinality.ManyToOne),
            Nav("b", "Line", "Order", Poly.DomainModeling.Ontology.RelationshipCardinality.ManyToOne)]);
        var storage = new StorageAnalyzer(domain, DomainModelAnalyzer.Analyze(domain)).Analyze().Entities.Single(e => e.Name == "Order");

        await Assert.That(InverseOf(storage, "Lines")).IsNull();
    }

    [Test]
    public async Task InversePropertyName_WhenAnotherNavigationClaimsThePeer_SkipsTheClaimedOne() {
        const string Dsl = """
            domain Test

            Line: entity {
              Qty: Number default(1)
              a: Order
              b: Order
            }

            Order: entity {
              lines: many Line via a
              extra: many Line
            }
            """;

        var order = Storage(Dsl, "Order");
        await Assert.That(InverseOf(order, "Lines")).IsEqualTo("A");
        await Assert.That(InverseOf(order, "Extra")).IsEqualTo("B");
    }

    // Mixed cardinality: the peer has one singular and one collection navigation back. Pinned as of this slice; the
    // exporter's CreateInWiring.FilledNavigation / FindInverseCollectionInfo answer differently (row S-11b unifies them).
    [Test]
    public async Task InversePropertyName_WhenPeerHasSingularAndCollectionBack_IsNullWhileTheExporterPicksOne() {
        const string Dsl = """
            domain Test

            Line: entity {
              Qty: Number default(1)
              order: Order
              orders: many Order
            }

            Order: entity {
              lines: many Line
            }
            """;

        await Assert.That(InverseOf(Storage(Dsl, "Order"), "Lines")).IsNull();

        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(Dsl).Parse());
        var line = result.Root.Types.OfType<Poly.DomainModeling.Ontology.Entity>().Single(e => e.Name == "Line");
        var order = result.Root.Types.OfType<Poly.DomainModeling.Ontology.Entity>().Single(e => e.Name == "Order");
        var lines = order.Navigations.Single(n => n.Name == "lines");
        await Assert.That(Poly.DomainModeling.Analysis.CreateInWiring.FilledNavigation(order, lines, line)?.Name).IsEqualTo("order");
        await Assert.That(Poly.DomainModeling.Lowering.DomainToCSharpExporter.FindInverseCollectionInfo(order, "Line").Unique?.Name).IsEqualTo("lines");
    }
}