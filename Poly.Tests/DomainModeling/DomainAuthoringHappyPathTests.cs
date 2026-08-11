using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Queries;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// Happy-path tests for the direct V3 domain API: bootstrap → evolve → query.
/// These tests lock the M2 authoring experience and use only V3 types.
/// </summary>
public class DomainAuthoringHappyPathTests {
    [Test]
    public async Task Bootstrap_Entity_Property_Stage_Action_AllSucceed() {
        var domain = DomainFactory.Create("Orders", builder =>
            builder.AddEntity("Order")
                   .AddPropertyToEntity("Order", new Property("Status", new DomainTypeReference("Text"), []))
                   .AddPropertyToEntity("Order", new Property("Total", new DomainTypeReference("Number"), []))
                   .AddStage("Order", "Draft")
                   .AddStage("Order", "Submitted")
                   .AddAction("Order", "Submit")
                   .AddActionToStage("Order", "Draft", "Submit"));

        var overview = DomainQueries.Overview(domain);
        await Assert.That(overview.EntityCount).IsEqualTo(1);
        await Assert.That(overview.PrimitiveTypeCount).IsGreaterThanOrEqualTo(9);

        var entity = DomainQueries.GetEntity(domain, "Order");
        await Assert.That(entity).IsNotNull();

        await Assert.That(entity!.Properties.Count).IsEqualTo(2);
        await Assert.That(entity.Properties[0].Name).IsEqualTo("Status");
        await Assert.That(entity.Properties[1].Name).IsEqualTo("Total");

        await Assert.That(entity.Stages.Count).IsEqualTo(2);
        await Assert.That(entity.Stages[0].Name).IsEqualTo("Draft");
        await Assert.That(entity.Stages[1].Name).IsEqualTo("Submitted");

        await Assert.That(entity.Actions.Count).IsEqualTo(1);
        await Assert.That(entity.Actions[0].Name).IsEqualTo("Submit");
    }

    [Test]
    public async Task MultiStepEvolve_ProducesCorrectRoot() {
        var domain = DomainFactory.Create("Orders");

        // First evolution: add entity
        var result1 = new DomainEvolution(domain).Evolve()
            .AddEntity("Order")
            .Apply();

        await Assert.That(result1.Succeeded).IsTrue();
        var order = result1.Root.Types.OfType<Entity>().Single();
        await Assert.That(order.Name).IsEqualTo("Order");

        // Second evolution: add properties + stages
        var result2 = new DomainEvolution(result1.Root).Evolve()
            .AddPropertyToEntity("Order", new Property("Status", new DomainTypeReference("Text"), []))
            .AddStage("Order", "Draft")
            .AddStage("Order", "Submitted")
            .AddAction("Order", "Submit")
            .Apply();

        await Assert.That(result2.Succeeded).IsTrue();
        var updated = result2.Root.Types.OfType<Entity>().Single();
        await Assert.That(updated.Properties.Count).IsEqualTo(1);
        await Assert.That(updated.Stages.Count).IsEqualTo(2);
        await Assert.That(updated.Actions.Count).IsEqualTo(1);

        // Original domain is unchanged
        var untouched = domain.Types.OfType<Entity>().ToList();
        await Assert.That(untouched).IsEmpty();
    }

    [Test]
    public async Task Query_GetEntity_ReturnsNull_ForMissingEntity() {
        var domain = DomainFactory.Create("Test");
        var detail = DomainQueries.GetEntity(domain, "NonExistent");

        await Assert.That(detail).IsNull();
    }

    [Test]
    public async Task Query_ListEntities_ReturnsCorrectNames() {
        var domain = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order")
                   .AddEntity("Customer")
                   .AddEntity("Product"));

        var names = DomainQueries.ListEntities(domain);
        await Assert.That(names.Count).IsEqualTo(3);
        await Assert.That(names).Contains("Order");
        await Assert.That(names).Contains("Customer");
        await Assert.That(names).Contains("Product");
    }

    [Test]
    public async Task Query_EntityDetail_IncludesNavigations_SourceAndTarget() {
        var domain = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order")
                   .AddEntity("Customer")
                   .AddRelationship("PlacedBy", "Order", "Customer",
                       RelationshipCardinality.ManyToOne, false));

        // Source view
        var orderDetail = DomainQueries.GetEntity(domain, "Order");
        await Assert.That(orderDetail).IsNotNull();
        await Assert.That(orderDetail!.Navigations.Count).IsEqualTo(1);
        var orderNav = orderDetail.Navigations[0];
        await Assert.That(orderNav.RelationshipName).IsEqualTo("PlacedBy");
        await Assert.That(orderNav.RelatedEntityName).IsEqualTo("Customer");
        await Assert.That(orderNav.Role).IsEqualTo("Source");
        await Assert.That(orderNav.Cardinality).IsEqualTo(RelationshipCardinality.ManyToOne.ToString());
        await Assert.That(orderNav.SourceOwnsTarget).IsFalse();

        // Target view
        var customerDetail = DomainQueries.GetEntity(domain, "Customer");
        await Assert.That(customerDetail).IsNotNull();
        await Assert.That(customerDetail!.Navigations.Count).IsEqualTo(1);
        var customerNav = customerDetail.Navigations[0];
        await Assert.That(customerNav.RelationshipName).IsEqualTo("PlacedBy");
        await Assert.That(customerNav.RelatedEntityName).IsEqualTo("Order");
        await Assert.That(customerNav.Role).IsEqualTo("Target");
        await Assert.That(customerNav.Cardinality).IsEqualTo(RelationshipCardinality.ManyToOne.ToString());
        await Assert.That(customerNav.SourceOwnsTarget).IsFalse();
    }

    [Test]
    public async Task Query_Overview_ReflectsEntityAndRelationshipCounts() {
        var domain = DomainFactory.Create("Demo", builder =>
            builder.AddEntity("Order")
                   .AddEntity("Customer")
                   .AddRelationship("CustomerOrders", "Customer", "Order",
                       RelationshipCardinality.OneToMany, sourceOwnsTarget: true));

        var overview = DomainQueries.Overview(domain);
        await Assert.That(overview.EntityCount).IsEqualTo(2);
        await Assert.That(overview.RelationshipCount).IsEqualTo(1);
        await Assert.That(overview.Name).IsEqualTo("Demo");
    }

    [Test]
    public async Task Query_AnalysisSummary_ReportsNoErrors_ForValidDomain() {
        var domain = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order")
                   .AddPropertyToEntity("Order", new Property("Status", new DomainTypeReference("Text"), [])));

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var summary = DomainQueries.GetAnalysisSummary(analysis);

        await Assert.That(summary.ErrorCount).IsEqualTo(0);
        await Assert.That(summary.HasStructuralFailure).IsFalse();
    }

    [Test]
    public async Task Query_AnalysisSummary_ReportsErrors_ForInvalidDomain() {
        // Duplicate entity names should produce structural errors
        var text = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var domain = DomainTestFactory.Create("Test",
        [
            new Entity("Duplicate", [], [], [], []),
            new Entity("Duplicate", [], [], [], []),
            text,
        ], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var summary = DomainQueries.GetAnalysisSummary(analysis);

        await Assert.That(summary.ErrorCount).IsGreaterThan(0);
        await Assert.That(summary.HasStructuralFailure).IsTrue();
    }

    [Test]
    public async Task Evolve_AddStage_Succeeds() {
        var domain = DomainFactory.Create("Test");

        var result = new DomainEvolution(domain).Evolve()
            .AddEntity("Order")
            .AddStage("Order", "Draft")
            .Apply();

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Evolve_RolledBack_OriginalDomainIsUnchanged() {
        var domain = DomainFactory.Create("Test");

        // Invalid evolution: duplicate entity name
        var result = new DomainEvolution(domain).Evolve()
            .AddEntity("Order")
            .AddEntity("Order") // duplicate
            .Apply();

        await Assert.That(result.WasRolledBack).IsTrue();
    }
}