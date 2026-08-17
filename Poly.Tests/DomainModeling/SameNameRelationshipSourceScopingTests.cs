using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// Locks the source-scoped relationship identity: the same relationship name may be
/// declared on different source entities, each resolving to its own relationship.
/// Covers parse → analyze → runtime (store links + quantifier policies) → export.
/// </summary>
public class SameNameRelationshipSourceScopingTests {
    private const string Dsl = """
        domain Test

        Machine: entity {
          Name: Text
          orders: many Order
          HasMachineOrders: policy { count orders > 0 }
        }

        Book: entity {
          Title: Text
          orders: many Order2
          HasBookOrders: policy { count orders > 0 }
        }

        Order: entity {
          Code: Text
        }

        Order2: entity {
          Sku: Text
        }
        """;

    private static Domain ParseAndBuild() {
        var changes = new PolyDslParser(Dsl).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded || result.Root is null)
            throw new InvalidOperationException($"Evolution failed: {result.FailureSummary}");
        return result.Root;
    }

    [Test]
    public async Task Parse_SameNavNameOnDifferentSources_ProducesTwoRelationships() {
        var domain = ParseAndBuild();
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var rels = analysis.GetAllRelationships(domain);
        await Assert.That(rels.Count).IsEqualTo(2);

        var machineOrders = rels.Single(r => r.Source.TypeName == "Machine");
        await Assert.That(machineOrders.Name).IsEqualTo("orders");
        await Assert.That(machineOrders.Target.TypeName).IsEqualTo("Order");

        var bookOrders = rels.Single(r => r.Source.TypeName == "Book");
        await Assert.That(bookOrders.Name).IsEqualTo("orders");
        await Assert.That(bookOrders.Target.TypeName).IsEqualTo("Order2");
    }

    [Test]
    public async Task Analyze_SourceScopedLookup_ResolvesEachSourceOwnRelationship() {
        var domain = ParseAndBuild();
        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.HasErrors).IsFalse();

        var rlm = analysis.GetRelationshipLookup(domain);
        await Assert.That(rlm).IsNotNull();

        await Assert.That(rlm!.TryGetRelationship("Machine", "orders", out var machineRel)).IsTrue();
        await Assert.That(machineRel!.Target.TypeName).IsEqualTo("Order");

        await Assert.That(rlm.TryGetRelationship("Book", "orders", out var bookRel)).IsTrue();
        await Assert.That(bookRel!.Target.TypeName).IsEqualTo("Order2");

        await Assert.That(ReferenceEquals(machineRel, bookRel)).IsFalse();

        await Assert.That(rlm.TryGetRelationship("Machine", "bogus", out _)).IsFalse();
    }

    [Test]
    public async Task Runtime_LinkAndQuantifierPolicy_AreSourceScoped() {
        var domain = ParseAndBuild();
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var store = new DomainInstanceStore();

        var machineEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Machine");
        var bookEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Book");
        var orderEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var order2Entity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order2");

        var machine = DomainEntityInstance.Create(machineEntity, domain: domain);
        var order = DomainEntityInstance.Create(orderEntity, domain: domain);
        var book = DomainEntityInstance.Create(bookEntity, domain: domain);
        var order2 = DomainEntityInstance.Create(order2Entity, domain: domain);
        store.Add(machine); store.Add(order); store.Add(book); store.Add(order2);

        // Each source links its own target via the SAME relationship name.
        store.Link("orders", machine, order);
        store.Link("orders", book, order2);

        var machinePolicy = machineEntity.Policies.Single(p => p.Name == "HasMachineOrders");
        var bookPolicy = bookEntity.Policies.Single(p => p.Name == "HasBookOrders");
        await Assert.That(machine.EvaluatePolicy(machinePolicy)).IsTrue();
        await Assert.That(book.EvaluatePolicy(bookPolicy)).IsTrue();

        // Discriminating: if the lookup were name-global, Book's 'orders' would resolve
        // to Machine→Order and its Order2 link would fail the target-type filter → false.
        var machineTargets = store.GetRelatedInstances("orders", machine);
        await Assert.That(machineTargets.Count).IsEqualTo(1);
        await Assert.That(machineTargets[0].Entity.Name).IsEqualTo("Order");

        var bookTargets = store.GetRelatedInstances("orders", book);
        await Assert.That(bookTargets.Count).IsEqualTo(1);
        await Assert.That(bookTargets[0].Entity.Name).IsEqualTo("Order2");
    }

    [Test]
    public async Task Parse_BackRefsWithSameNameOnDifferentChildren_Succeeds() {
        // The exact case that broke modeling before source-scoping: two children each
        // back-reference their parent via a nav named 'order'.
        // back-reference their parent via a nav named 'order'.
        var poly = """
            domain Test

            Order: entity {
              lines: many OrderLine
              notes: many Note
            }

            OrderLine: entity {
              Sku: Text
              order: Order
            }

            Note: entity {
              Body: Text
              order: Order
            }
            """;

        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Relationships().Count).IsEqualTo(4);
        await Assert.That(result.Relationships().Count(r => r.Name == "order")).IsEqualTo(2);
        await Assert.That(result.Analysis.HasErrors).IsFalse();

        // Round-trips: the printer emits the same-name navs per source entity.
        var printed = new DomainDslPrinter().Print(result.Root);
        var reparsed = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(new PolyDslParser(printed).Parse());
        await Assert.That(reparsed.Succeeded).IsTrue();
        await Assert.That(reparsed.Relationships().Count(r => r.Name == "order")).IsEqualTo(2);
    }

    [Test]
    public async Task Model_Relationships_AreDerivedFlatten_OfEntityNavs() {
        // Storage is entity-owned navs; the analysis catalog derives relationships from them.
        var order = new Entity("Order", [], [], [], []);
        var customer = new Entity("Customer", [], [], [], []);
        var rel = new Relationship("orders",
            new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Test", [customer with { Navigations = [rel] }, order]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.GetAllRelationships(domain).Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(analysis.GetAllRelationships(domain)[0], rel)).IsTrue();
        var customerInDomain = domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        await Assert.That(customerInDomain.Navigations.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(customerInDomain.Navigations[0], rel)).IsTrue();
    }

    [Test]
    public async Task Model_ThreeArgCtor_RedistributesOntoEntityNavs() {
        // Legacy bridge: the 3-arg constructor attaches relationships to their source entity.
        var order = new Entity("Order", [], [], [], []);
        var customer = new Entity("Customer", [], [], [], []);
        var rel = new Relationship("orders",
            new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Test", [customer, order], [rel]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.GetAllRelationships(domain).Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(analysis.GetAllRelationships(domain)[0], rel)).IsTrue();
        var customerInDomain = domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        await Assert.That(customerInDomain.Navigations.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(customerInDomain.Navigations[0], rel)).IsTrue();
    }

    [Test]
    public async Task Model_ThreeArgCtor_WithOrphanRelationship_FailsClosed() {
        // G1: a relationship whose source entity is not in the domain must not be
        // silently dropped by the redistribution bridge — fail loud.
        var order = new Entity("Order", [], [], [], []);
        var ghost = new Relationship("orders",
            new DomainTypeReference("Ghost"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);

        await Assert.That(() => DomainTestFactory.Create("Test", [order], [ghost]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Mutation_AddAndRemoveRelationship_OperatesOnEntityNavs() {
        var domain = ParseAndBuild();
        var source = domain.Types.OfType<Entity>().Single(e => e.Name == "Machine");
        var before = source.Navigations.Count;

        var added = new DomainEvolution(domain).Apply([new AddRelationshipChange(
            "extra", new DomainTypeReference("Machine"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToOne, [])]);
        await Assert.That(added.Succeeded).IsTrue();
        var addedEntity = added.Root!.Types.OfType<Entity>().Single(e => e.Name == "Machine");
        await Assert.That(addedEntity.Navigations.Count).IsEqualTo(before + 1);
        await Assert.That(added.Relationships().Any(r => r.Name == "extra")).IsTrue();

        var removed = new DomainEvolution(added.Root).Apply([new RemoveRelationshipChange("Machine", "extra")]);
        await Assert.That(removed.Succeeded).IsTrue();
        var removedEntity = removed.Root!.Types.OfType<Entity>().Single(e => e.Name == "Machine");
        await Assert.That(removedEntity.Navigations.Count).IsEqualTo(before);
        await Assert.That(removed.Relationships().Any(r => r.Name == "extra")).IsFalse();
    }

    [Test]
    public async Task Export_EachSourceEmitsOwnCreateNavMethod() {
        var domain = ParseAndBuild();
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var types = new DomainToCSharpExporter().Export(domain, analysis);

        var machine = types.Single(t => t.Name == "Machine");
        var book = types.Single(t => t.Name == "Book");

        // Same method name on different classes — no cross-entity collision.
        await Assert.That(machine.Methods?.Any(m => m.Name == "CreateOrders")).IsTrue();
        await Assert.That(book.Methods?.Any(m => m.Name == "CreateOrders")).IsTrue();
        await Assert.That(machine.Methods!.Single(m => m.Name == "CreateOrders").Parameters?.Count).IsEqualTo(1);
        await Assert.That(book.Methods!.Single(m => m.Name == "CreateOrders").Parameters?.Count).IsEqualTo(1);
    }
}