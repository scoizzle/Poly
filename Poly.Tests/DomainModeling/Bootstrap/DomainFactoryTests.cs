using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Bootstrap;

/// <summary>
/// Tests for the V3 bootstrap layer — DomainFactory and CanonicalBuiltInTypeCatalog.
/// Verifies that domains can be created with built-in primitives and clean analysis,
/// without referencing Poly.Data.Modeling.
/// </summary>
public class DomainFactoryTests {
    [Test]
    public async Task DomainFactory_Create_HasBuiltInPrimitives() {
        var domain = DomainFactory.Create("Test");

        await Assert.That(domain.Name).IsEqualTo("Test");
        await Assert.That(domain.Types).IsNotEmpty();

        var primitives = domain.Types.OfType<PrimitiveType>().ToList();
        await Assert.That(primitives.Count).IsGreaterThanOrEqualTo(9);

        var names = primitives.Select(p => p.Name).ToHashSet();
        await Assert.That(names).Contains("Boolean");
        await Assert.That(names).Contains("Number");
        await Assert.That(names).Contains("Text");
        await Assert.That(names).Contains("Uuid");
        await Assert.That(names).Contains("Binary");
        await Assert.That(names).Contains("Date");
        await Assert.That(names).Contains("Time");
        await Assert.That(names).Contains("DateTime");
        await Assert.That(names).Contains("Duration");
    }

    [Test]
    public async Task DomainFactory_Create_AnalysisPasses() {
        var domain = DomainFactory.Create("Test");
        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(analysis.HasStructuralFailure).IsFalse();
    }

    [Test]
    public async Task DomainFactory_Create_ReturnsDifferentRootOnEachCall() {
        var domain1 = DomainFactory.Create("A");
        var domain2 = DomainFactory.Create("B");

        await Assert.That(domain1).IsNotSameReferenceAs(domain2);
        await Assert.That(domain1.Name).IsEqualTo("A");
        await Assert.That(domain2.Name).IsEqualTo("B");
    }

    [Test]
    public async Task DomainFactory_Create_WithAdditionalChanges() {
        var domain = DomainFactory.Create("Orders",
            new AddEntityChange("Order", []));

        var entities = domain.Types.OfType<Entity>().ToList();
        await Assert.That(entities.Count).IsEqualTo(1);
        await Assert.That(entities[0].Name).IsEqualTo("Order");

        // Builtins still present
        var primitives = domain.Types.OfType<PrimitiveType>().ToList();
        await Assert.That(primitives.Count).IsGreaterThanOrEqualTo(9);
    }

    [Test]
    public async Task DomainFactory_Create_WithConfigureCallback() {
        var domain = DomainFactory.Create("Orders", builder =>
            builder.AddEntity("Order")
                   .AddPropertyToEntity("Order", new Property("Status", new DomainTypeReference("Text"), []))
                   .AddStage("Order", "Draft")
                   .AddAction("Order", "Submit"));

        var entity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order");
        await Assert.That(entity.Properties.Count).IsEqualTo(1);
        await Assert.That(entity.Properties[0].Name).IsEqualTo("Status");
        await Assert.That(entity.Stages.Count).IsEqualTo(1);
        await Assert.That(entity.Stages[0].Name).IsEqualTo("Draft");
        await Assert.That(entity.Actions.Count).IsEqualTo(1);
        await Assert.That(entity.Actions[0].Name).IsEqualTo("Submit");
    }

    [Test]
    public async Task CanonicalBuiltInTypeCatalog_CreateChanges_ReturnsCoreFive() {
        var changes = CanonicalBuiltInTypeCatalog.CreateChanges();

        await Assert.That(changes.Count).IsEqualTo(5);
        await Assert.That(changes.All(c => c is DomainChange)).IsTrue();

        var names = changes.OfType<AddPrimitiveTypeChange>().Select(c => c.Name).ToHashSet();
        await Assert.That(names.Count).IsEqualTo(5);
        await Assert.That(names).Contains("Boolean");
        await Assert.That(names).Contains("Number");
        await Assert.That(names).Contains("Text");
        await Assert.That(names).Contains("Uuid");
        await Assert.That(names).Contains("Binary");
        await Assert.That(names).DoesNotContain("Date");
        await Assert.That(names).DoesNotContain("DateTime");
    }

    [Test]
    public async Task CanonicalBuiltInTypeCatalog_ApplyTo_SeedsDomain() {
        var empty = DomainTestFactory.Create("Test", [], []);
        var seeded = CanonicalBuiltInTypeCatalog.ApplyTo(empty);

        await Assert.That(seeded).IsNotSameReferenceAs(empty);
        await Assert.That(seeded.Types.OfType<PrimitiveType>().Count()).IsEqualTo(5);
        var names = seeded.Types.OfType<PrimitiveType>().Select(p => p.Name).ToHashSet();
        await Assert.That(names).DoesNotContain("Date");
        await Assert.That(names).DoesNotContain("DateTime");
    }

    [Test]
    public async Task DomainFactory_Create_EmptyName_Throws() {
        await Assert.That(() => DomainFactory.Create(""))
            .Throws<ArgumentException>();

        await Assert.That(() => DomainFactory.Create(" "))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task DomainFactory_Create_WithFailingConfigure_ReturnsRolledBack() {
        // Duplicate entity name is a reliable structural failure
        var domain = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order")
                   .AddEntity("Order")); // duplicate → structural failure

        // Builtins should still be present despite the configure failure
        var primitives = domain.Types.OfType<PrimitiveType>().ToList();
        await Assert.That(primitives.Count).IsGreaterThanOrEqualTo(9);

        // The duplicate entity should NOT have been added
        var orders = domain.Types.OfType<Entity>().Where(e => e.Name == "Order").ToList();
        await Assert.That(orders.Count).IsEqualTo(0);
    }
}