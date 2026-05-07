using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.Recipes;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Introspection;

namespace Poly.Tests.Data.Modeling.Recipes;

/// <summary>
/// Contract tests verifying that scaffold recipes:
/// 1. Build valid domain structures atomically
/// 2. Produce identical analyzer outcomes vs. low-level mutations
/// 3. Validate preconditions and fail fast on conflicts
/// </summary>
public class ScaffoldRecipeTests {

    // ── Entity Scaffold Recipe ────────────────────────────────────────────────

    [Test]
    public async Task EntityScaffold_BuildsAtomically() {
        var domain = new Domain("TestDomain");
        var stringType = new Primitive(domain, "String", TypeCategory.Text);
        domain.AddType(stringType);

        var recipe = new EntityScaffoldRecipe("Order")
            .WithProperty("id", stringType)
            .WithProperty("customerId", stringType)
            .WithStage("Draft")
            .WithStage("Confirmed");

        recipe.BuildInto(domain);

        // Verify entity exists
        var order = domain.Types.OfType<Entity>().FirstOrDefault(e => e.Name == "Order" && !(e is Relationship));
        await Assert.That(order).IsNotNull();
        await Assert.That(order!.Properties.Count).IsEqualTo(2);
        await Assert.That(order.Stages.Count).IsEqualTo(2);

        // Verify properties by name
        var idProp = order.Properties.FirstOrDefault(p => p.Name == "id");
        var customerIdProp = order.Properties.FirstOrDefault(p => p.Name == "customerId");
        await Assert.That(idProp).IsNotNull();
        await Assert.That(customerIdProp).IsNotNull();

        // Verify stages by name
        var draftStage = order.Stages.FirstOrDefault(s => s.Name == "Draft");
        var confirmedStage = order.Stages.FirstOrDefault(s => s.Name == "Confirmed");
        await Assert.That(draftStage).IsNotNull();
        await Assert.That(confirmedStage).IsNotNull();
    }

    [Test]
    public async Task EntityScaffold_ProducesIdenticalAnalyzerOutcome() {
        var stringType = new Primitive(new Domain("Dummy"), "String", TypeCategory.Text);

        // Path 1: Using recipe
        var recipeDomain = new Domain("TestDomain");
        recipeDomain.AddType(stringType);

        var recipe = new EntityScaffoldRecipe("Order")
            .WithProperty("id", stringType)
            .WithStage("Draft");
        recipe.BuildInto(recipeDomain);

        var recipeAnalyzer = new DomainModelAnalyzer();
        var recipeResult = recipeAnalyzer.Analyze(recipeDomain);

        // Path 2: Using low-level mutations
        var lowLevelDomain = new Domain("TestDomain");
        lowLevelDomain.AddType(stringType);

        var mutation = lowLevelDomain.CreateMutation();
        var entity = new Entity(lowLevelDomain, "Order");
        mutation.AddType(entity);
        mutation.AddProperty(entity, new Property(lowLevelDomain, "id", stringType));
        mutation.AddStage(entity, new Stage(lowLevelDomain, "Draft"));
        mutation.Apply();

        var lowLevelAnalyzer = new DomainModelAnalyzer();
        var lowLevelResult = lowLevelAnalyzer.Analyze(lowLevelDomain);

        // Both paths must produce equivalent diagnostics
        await Assert.That(recipeResult.Diagnostics.Count).IsEqualTo(lowLevelResult.Diagnostics.Count);

        // Collect diagnostic codes from both paths
        var recipeCodes = recipeResult.Diagnostics.Select(d => d.Code).OrderBy(c => c).ToArray();
        var lowLevelCodes = lowLevelResult.Diagnostics.Select(d => d.Code).OrderBy(c => c).ToArray();

        await Assert.That(recipeCodes).IsEqualTo(lowLevelCodes);
    }

    [Test]
    public async Task EntityScaffold_ValidatesPreconditions() {
        var domain = new Domain("TestDomain");

        // Valid: recipe with no properties or stages
        var emptyRecipe = new EntityScaffoldRecipe("EmptyEntity");
        emptyRecipe.BuildInto(domain);

        // Verify entity was created
        var emptyEntity = domain.Types.OfType<Entity>().FirstOrDefault(e => e.Name == "EmptyEntity" && !(e is Relationship));
        await Assert.That(emptyEntity).IsNotNull();
    }

    // ── Relationship Scaffold Recipe ──────────────────────────────────────────

    [Test]
    public async Task RelationshipScaffold_BuildsAtomically() {
        var domain = new Domain("TestDomain");

        // Create source and target entities
        var source = new Entity(domain, "Order");
        domain.AddType(source);

        var target = new Entity(domain, "Customer");
        domain.AddType(target);

        // Build relationship via recipe  (OneToMany is required for ownership relationships)
        var recipe = new RelationshipScaffoldRecipe("OrderToCustomer")
            .WithSource(source, ownsTarget: true)
            .WithTarget(target)
            .WithCardinality(RelationshipCardinality.OneToMany);

        recipe.BuildInto(domain);

        // Verify relationship exists (check both Objects and Relationships collection)
        var relationship = domain.Relationships.FirstOrDefault(r => r.Name == "OrderToCustomer");
        await Assert.That(relationship).IsNotNull();
        await Assert.That(relationship!.Source).IsEqualTo(source);
        await Assert.That(relationship.Target).IsEqualTo(target);
        await Assert.That(relationship.Cardinality).IsEqualTo(RelationshipCardinality.OneToMany);
        await Assert.That(relationship.SourceOwnsTarget).IsTrue();

        // Verify relationship is linked to source entity
        await Assert.That(source.Relationships.Contains(relationship)).IsTrue();
    }

    [Test]
    public async Task RelationshipScaffold_ProducesIdenticalAnalyzerOutcome() {
        // Path 1: Using recipe
        var recipeDomain = new Domain("TestDomain");
        var recipeSource = new Entity(recipeDomain, "Order");
        recipeDomain.AddType(recipeSource);
        var recipeTarget = new Entity(recipeDomain, "Customer");
        recipeDomain.AddType(recipeTarget);

        var recipe = new RelationshipScaffoldRecipe("OrderToCustomer")
            .WithSource(recipeSource, ownsTarget: true)
            .WithTarget(recipeTarget)
            .WithCardinality(RelationshipCardinality.OneToMany);
        recipe.BuildInto(recipeDomain);

        var recipeAnalyzer = new DomainModelAnalyzer();
        var recipeResult = recipeAnalyzer.Analyze(recipeDomain);

        // Path 2: Using low-level mutations
        var lowLevelDomain = new Domain("TestDomain");
        var lowLevelSource = new Entity(lowLevelDomain, "Order");
        lowLevelDomain.AddType(lowLevelSource);
        var lowLevelTarget = new Entity(lowLevelDomain, "Customer");
        lowLevelDomain.AddType(lowLevelTarget);

        var mutation = lowLevelDomain.CreateMutation();
        var relationship = new Relationship(
            lowLevelDomain,
            "OrderToCustomer",
            lowLevelSource,
            lowLevelTarget,
            RelationshipCardinality.OneToMany,
            sourceOwnsTarget: true
        );
        mutation.AddRelationship(relationship);
        mutation.AddEntityRelationship(lowLevelSource, relationship);
        mutation.Apply();

        var lowLevelAnalyzer = new DomainModelAnalyzer();
        var lowLevelResult = lowLevelAnalyzer.Analyze(lowLevelDomain);

        // Both paths must produce equivalent diagnostics
        await Assert.That(recipeResult.Diagnostics.Count).IsEqualTo(lowLevelResult.Diagnostics.Count);

        var recipeCodes = recipeResult.Diagnostics.Select(d => d.Code).OrderBy(c => c).ToArray();
        var lowLevelCodes = lowLevelResult.Diagnostics.Select(d => d.Code).OrderBy(c => c).ToArray();

        await Assert.That(recipeCodes).IsEqualTo(lowLevelCodes);
    }

    [Test]
    public async Task RelationshipScaffold_FailsWhenSourceMissing() {
        var domain = new Domain("TestDomain");
        var target = new Entity(domain, "Customer");
        domain.AddType(target);

        var recipe = new RelationshipScaffoldRecipe("OrderToCustomer")
            .WithTarget(target)
            .WithCardinality(RelationshipCardinality.OneToMany);

        await Assert.That(() => recipe.BuildInto(domain))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RelationshipScaffold_FailsWhenTargetMissing() {
        var domain = new Domain("TestDomain");
        var source = new Entity(domain, "Order");
        domain.AddType(source);

        var recipe = new RelationshipScaffoldRecipe("OrderToCustomer")
            .WithSource(source, ownsTarget: true)
            .WithCardinality(RelationshipCardinality.OneToMany);

        await Assert.That(() => recipe.BuildInto(domain))
            .Throws<InvalidOperationException>();
    }

    // ── Workflow Scaffold Recipe ──────────────────────────────────────────────

    [Test]
    public async Task WorkflowScaffold_BuildsAtomically() {
        var domain = new Domain("TestDomain");

        var recipe = new WorkflowScaffoldRecipe("OrderProcessing")
            .WithEntity("Order")
            .WithEntity("Fulfillment")
            .WithStage("Draft", "Submitted", "Approved")
            .WithStateTransition("Draft", "Submitted")
            .WithStateTransition("Submitted", "Approved");

        recipe.BuildInto(domain);

        // Verify entities exist
        var orderEntity = domain.Types.OfType<Entity>().FirstOrDefault(e => e.Name == "Order" && !(e is Relationship));
        var fulfillmentEntity = domain.Types.OfType<Entity>().FirstOrDefault(e => e.Name == "Fulfillment" && !(e is Relationship));

        await Assert.That(orderEntity).IsNotNull();
        await Assert.That(fulfillmentEntity).IsNotNull();

        // Verify each entity has all stages
        await Assert.That(orderEntity!.Stages.Count).IsEqualTo(3);
        await Assert.That(fulfillmentEntity!.Stages.Count).IsEqualTo(3);

        // Verify stage names
        var orderStageNames = orderEntity.Stages.Select(s => s.Name).OrderBy(n => n).ToList();
        var fulfillmentStageNames = fulfillmentEntity.Stages.Select(s => s.Name).OrderBy(n => n).ToList();

        await Assert.That(orderStageNames[0]).IsEqualTo("Approved");
        await Assert.That(orderStageNames[1]).IsEqualTo("Draft");
        await Assert.That(orderStageNames[2]).IsEqualTo("Submitted");

        await Assert.That(fulfillmentStageNames[0]).IsEqualTo("Approved");
        await Assert.That(fulfillmentStageNames[1]).IsEqualTo("Draft");
        await Assert.That(fulfillmentStageNames[2]).IsEqualTo("Submitted");
    }

    [Test]
    public async Task WorkflowScaffold_ProducesIdenticalAnalyzerOutcome() {
        // Path 1: Using recipe
        var recipeDomain = new Domain("TestDomain");
        var recipe = new WorkflowScaffoldRecipe("OrderProcessing")
            .WithEntity("Order")
            .WithStage("Draft", "Submitted")
            .WithStateTransition("Draft", "Submitted");
        recipe.BuildInto(recipeDomain);

        var recipeAnalyzer = new DomainModelAnalyzer();
        var recipeResult = recipeAnalyzer.Analyze(recipeDomain);

        // Path 2: Using low-level mutations
        var lowLevelDomain = new Domain("TestDomain");
        var mutation = lowLevelDomain.CreateMutation();

        var orderEntity = new Entity(lowLevelDomain, "Order");
        mutation.AddType(orderEntity);
        mutation.AddStage(orderEntity, new Stage(lowLevelDomain, "Draft"));
        mutation.AddStage(orderEntity, new Stage(lowLevelDomain, "Submitted"));

        mutation.Apply();

        var lowLevelAnalyzer = new DomainModelAnalyzer();
        var lowLevelResult = lowLevelAnalyzer.Analyze(lowLevelDomain);

        // Both paths must produce equivalent diagnostics
        await Assert.That(recipeResult.Diagnostics.Count).IsEqualTo(lowLevelResult.Diagnostics.Count);

        var recipeCodes = recipeResult.Diagnostics.Select(d => d.Code).OrderBy(c => c).ToArray();
        var lowLevelCodes = lowLevelResult.Diagnostics.Select(d => d.Code).OrderBy(c => c).ToArray();

        await Assert.That(recipeCodes).IsEqualTo(lowLevelCodes);
    }

    [Test]
    public async Task WorkflowScaffold_FailsWhenEntitiesMissing() {
        var domain = new Domain("TestDomain");

        var recipe = new WorkflowScaffoldRecipe("OrderProcessing")
            .WithStage("Draft", "Submitted");

        await Assert.That(() => recipe.BuildInto(domain))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task WorkflowScaffold_FailsWhenStagesMissing() {
        var domain = new Domain("TestDomain");

        var recipe = new WorkflowScaffoldRecipe("OrderProcessing")
            .WithEntity("Order");

        await Assert.That(() => recipe.BuildInto(domain))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task WorkflowScaffold_FailsOnInvalidTransition() {
        var domain = new Domain("TestDomain");

        var recipe = new WorkflowScaffoldRecipe("OrderProcessing")
            .WithEntity("Order")
            .WithStage("Draft", "Submitted")
            .WithStateTransition("Draft", "InvalidStage");

        await Assert.That(() => recipe.BuildInto(domain))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Recipe_NamePropertyReflectsContent() {
        var recipe1 = new EntityScaffoldRecipe("Order");
        await Assert.That(recipe1.Name).Contains("Order");

        var recipe2 = new RelationshipScaffoldRecipe("OrderToCustomer");
        await Assert.That(recipe2.Name).Contains("OrderToCustomer");

        var recipe3 = new WorkflowScaffoldRecipe("OrderProcessing");
        await Assert.That(recipe3.Name).Contains("OrderProcessing");
    }
}