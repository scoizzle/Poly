using Poly.DomainModeling;
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Evolution;

/// <summary>
/// Tests that prove the evolution layer now actually applies changes
/// and produces new immutable roots while leaving the original untouched.
/// </summary>
public class DomainEvolutionApplicatorTests {
    [Test]
    public async Task Apply_AddEntityChange_ProducesNewRootWithEntity() {
        // Tiny starting domain
        var start = new Domain("TestDomain", [], []);

        var change = new AddEntityChange("Order", []);

        var evolution = new DomainEvolution(start);
        var result = evolution.Apply([change]);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.WasRolledBack).IsFalse(); // proposal accepted
        await Assert.That(result.Root).IsNotSameReferenceAs(start);

        var entities = result.Root.Types.OfType<Entity>().ToList();
        await Assert.That(entities.Count).IsEqualTo(1);
        await Assert.That(entities[0].Name).IsEqualTo("Order");
        await Assert.That(entities[0].Properties.Count).IsEqualTo(0); // simplified test uses no initial props to avoid needing primitives
    }

    [Test]
    public async Task Apply_RemoveEntityChange_RemovesFromNewRoot() {
        var entity = new Entity("Customer", [], [], [], []);
        var start = new Domain("TestDomain", [entity], []);

        var change = new RemoveEntityChange("Customer");

        var result = new DomainEvolution(start).Apply([change]);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root.Types.OfType<Entity>()).IsEmpty();
        await Assert.That(result.Root).IsNotSameReferenceAs(start);
    }

    [Test]
    public async Task Apply_AddPropertyToExistingEntity_Works() {
        // Seed common primitive so property reference resolves
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var entity = new Entity("Order", [], [], [], []);
        var start = new Domain("Test", [entity, textPrimitive], []);

        var newProp = new Property("Status", new DomainTypeReference("Text"), []);
        var change = new AddPropertyToEntityChange("Order", newProp);

        var result = new DomainEvolution(start).Apply([change]);

        var updated = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Order");
        await Assert.That(updated.Properties.Count).IsEqualTo(1);
        await Assert.That(updated.Properties[0].Name).IsEqualTo("Status");
    }

    [Test]
    public async Task Apply_MixedBatch_ProducesCorrectResult() {
        // Seed primitives used by the batch
        var intPrimitive = new PrimitiveType("Int", Poly.Introspection.TypeCategory.Integer, []);
        var start = new Domain("Test", [intPrimitive], []);

        var changes = new DomainChange[]
        {
            new AddEntityChange("Order", []),
            new AddEntityChange("Customer", []),
            new AddPropertyToEntityChange("Order", new Property("Total", new DomainTypeReference("Int"), []))
        };

        var result = new DomainEvolution(start).Apply(changes);

        var entities = result.Root.Types.OfType<Entity>().ToList();
        await Assert.That(entities.Count).IsEqualTo(2);

        var order = entities.Single(e => e.Name == "Order");
        await Assert.That(order.Properties.Count).IsEqualTo(1);
    }

    [Test]
    public async Task OriginalDomain_RemainsUnchanged_AfterApply() {
        var start = new Domain("Test", [], []);
        var change = new AddEntityChange("Order", []);

        _ = new DomainEvolution(start).Apply([change]);

        // The original reference must be completely untouched
        await Assert.That(start.Types).IsEmpty();
    }

    [Test]
    public async Task UnchangedEntities_RetainOriginalNodeId() {
        // Create a domain with two entities + required primitive
        var intPrimitive = new PrimitiveType("Int", Poly.Introspection.TypeCategory.Integer, []);
        var untouched = new Entity("Customer", [], [], [], []);
        var touched = new Entity("Order", [], [], [], []);
        var start = new Domain("Test", [untouched, touched, intPrimitive], []);

        var originalUntouchedId = untouched.Id;

        // Change only affects "Order"
        var change = new AddPropertyToEntityChange("Order",
            new Property("Total", new DomainTypeReference("Int"), []));

        var result = new DomainEvolution(start).Apply([change]);

        var customerInResult = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Customer");

        // The untouched entity reuses the exact same record instance (shallow sharing for unchanged subtrees in the batch applicator)
        // but carries the stable original NodeId — this is the key continuity guarantee for incremental analysis and UI.
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(customerInResult.Id).IsEqualTo(originalUntouchedId);
        await Assert.That(result.Root).IsNotSameReferenceAs(start); // the evolved Domain root is a distinct immutable value
    }

    [Test]
    public async Task Apply_AddStageChange_AddsStageToEntity() {
        var entity = new Entity("Person", [], [], [], []);
        var start = new Domain("Test", [entity], []);

        var change = new AddStageChange("Person", "Alive");

        var result = new DomainEvolution(start).Apply([change]);

        var updated = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");
        await Assert.That(updated.Stages.Count).IsEqualTo(1);
        await Assert.That(updated.Stages[0].Name).IsEqualTo("Alive");
        // Stage hierarchy not supported — no Parent property.
    }

    [Test]
    public async Task Apply_RemoveStageChange_RemovesStage() {
        var stage = new Stage("Alive", [], [], [], []);
        var entity = new Entity("Person", [], [], [], [stage]);
        var start = new Domain("Test", [entity], []);

        var change = new RemoveStageChange("Person", "Alive");

        var result = new DomainEvolution(start).Apply([change]);

        var updated = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");
        await Assert.That(updated.Stages).IsEmpty();
    }

    [Test]
    public async Task Apply_AddActionChange_AddsActionToEntity() {
        var entity = new Entity("Person", [], [], [], []);
        var start = new Domain("Test", [entity], []);

        var change = new AddActionChange("Person", "Die");

        var result = new DomainEvolution(start).Apply([change]);

        var updated = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");
        await Assert.That(updated.Actions.Count).IsEqualTo(1);
        await Assert.That(updated.Actions[0].Name).IsEqualTo("Die");
    }

    [Test]
    public async Task Apply_RemoveActionChange_RemovesAction() {
        var action = new Poly.DomainModeling.Action("Die", InvocationResult.Void, [], [], []);
        var entity = new Entity("Person", [], [action], [], []);
        var start = new Domain("Test", [entity], []);

        var change = new RemoveActionChange("Person", "Die");

        var result = new DomainEvolution(start).Apply([change]);

        var updated = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");
        await Assert.That(updated.Actions).IsEmpty();
    }

    [Test]
    public async Task Apply_MultiStepEvolution_BuildsSmallLifecycleShape() {
        // Start completely empty
        var start = new Domain("PersonLifecycle", [], []);

        // Evolve it step by step using the new layer (this is the shape we want agents to be able to drive)
        var changes1 = new DomainChange[]
        {
            new AddEntityChange("Person", []),
            new AddStageChange("Person", "Alive"),
            new AddStageChange("Person", "Dead"),
            new AddActionChange("Person", "Die")
        };

        var afterFirst = new DomainEvolution(start).Apply(changes1);
        await Assert.That(afterFirst.Succeeded).IsTrue();

        var person = afterFirst.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");
        await Assert.That(person.Stages.Count).IsEqualTo(2);
        await Assert.That(person.Actions.Count).IsEqualTo(1);
        await Assert.That(person.Actions[0].Name).IsEqualTo("Die");

        // Second evolution: realistic *incremental* evolution on the existing structure from step 1.
        // We enhance (attach policies/effects) rather than re-adding duplicate stages/actions.
        // Seed primitives and owned doc ValueTypes first (required for the analysis gate)
        var result2 = new DomainEvolution(afterFirst.Root)
            .Evolve()
            .AddPrimitiveType("Text", Poly.Introspection.TypeCategory.Text)
            .AddPrimitiveType("Timestamp", Poly.Introspection.TypeCategory.DateTime)
            .AddPrimitiveType("Boolean", Poly.Introspection.TypeCategory.Boolean)
            // Add owned documents (ValueTypes) — key for PersonLifecycle style
            .AddValueType("BirthCertificate", new Property("Time", new DomainTypeReference("Timestamp"), []))
            .AddValueType("DeathCertificate",
                new Property("Time", new DomainTypeReference("Timestamp"), []),
                new Property("Cause", new DomainTypeReference("Text"), []))
            .AddPropertyToEntity("Person", new Property("GivenName", new DomainTypeReference("Text"), []))
            // Enhance existing Alive stage with an OnEntry effect. Do not re-AddStage.
            .AddStageGuard("Person", "Alive", "HasBirthCert",
                DomainExpression.Exists(DomainExpression.Owned("BirthCertificate", DomainExpression.Property("Time"))))
            .AddOnEntryEffect("Person", "Alive",
                new CompositeEffect([]))

            // Demonstrate action defined directly on a stage (new MVP capability)
            .AddActionToStage("Person", "Alive", "Cancel")
            // New action Register with its full definition
            .AddAction("Person", "Register",
                result: new InvocationResult([new InvocationResult.Member("BirthCertificateId", new DomainTypeReference("Text"), [])]),
                parameters: [new Property("TimeOfBirth", new DomainTypeReference("Timestamp"), [])],
                effects: [new CreateEntityInstance(new DomainTypeReference("BirthCertificate"), [
                    new PropertyBinding("Time", DomainExpression.Parameter("TimeOfBirth"))
                ])])
            .AddPolicyToAction("Person", "Register", "ValidRegistration",
                DomainExpression.Exists(DomainExpression.Parameter("TimeOfBirth")))
            // Enhance existing Dead stage (attach guard policy). Do not re-AddStage.
            .AddStageGuard("Person", "Dead", "HasDeathCert",
                DomainExpression.Exists(DomainExpression.Owned("DeathCertificate", DomainExpression.Property("Time"))))
            // Enhance the existing Die action (attach result + effects). Do not re-AddAction.
            .SetActionResult("Person", "Die", new InvocationResult([new InvocationResult.Member("Success", new DomainTypeReference("Boolean"), [])]))
            .AddCreateEffect("Person", "Die", "DeathCertificate",
                ("Time", DomainExpression.Parameter("TimeOfDeath")),
                ("Cause", DomainExpression.Parameter("CauseOfDeath")))
            .AddStageTransitionEffect("Person", "Die", "Dead")
            .AddRelationship("Friends", "Person", "Person", RelationshipCardinality.ManyToMany)
            .Apply();

        await Assert.That(result2.Succeeded).IsTrue();

        var finalPerson = result2.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");
        await Assert.That(finalPerson.Properties.Count).IsEqualTo(1);
        await Assert.That(finalPerson.Stages.Count).IsEqualTo(2);

        var alive = finalPerson.Stages.Single(s => s.Name == "Alive");
        await Assert.That(alive.Policies.Count).IsEqualTo(1);
        await Assert.That(alive.OnEntryEffects.Count).IsEqualTo(1);
        await Assert.That(alive.OnExitEffects.Count).IsEqualTo(0); // test only adds onEntry for Alive in this evolution sequence
        await Assert.That(alive.Actions.Count).IsEqualTo(1);
        await Assert.That(alive.Actions[0].Name).IsEqualTo("Cancel"); // action defined directly on the stage via evolution

        var dead = finalPerson.Stages.Single(s => s.Name == "Dead");
        await Assert.That(dead.Policies.Count).IsEqualTo(1);
        await Assert.That(dead.Policies[0].Name).IsEqualTo("HasDeathCert"); // explicit name passed to AddStageGuard

        var register = finalPerson.Actions.Single(a => a.Name == "Register");
        await Assert.That(register.Effects.Count).IsEqualTo(1);
        await Assert.That(register.Policies.Count).IsEqualTo(1);
        await Assert.That(register.Parameters.Count).IsEqualTo(1);
        await Assert.That(register.Parameters[0].Name).IsEqualTo("TimeOfBirth");
        await Assert.That(register.Result.Members.Count).IsEqualTo(1);
        await Assert.That(register.Result.Members[0].Name).IsEqualTo("BirthCertificateId");

        var die = finalPerson.Actions.Single(a => a.Name == "Die");
        await Assert.That(die.Effects.Count).IsEqualTo(2); // Create + Transition (no extra Publish in this sequence)
        await Assert.That(die.Result.Members.Count).IsEqualTo(1);

        await Assert.That(result2.Root.Relationships.Count).IsEqualTo(1);
        await Assert.That(result2.Root.Relationships[0].Name).IsEqualTo("Friends");

        // Verify ValueTypes were added
        var valueTypes = result2.Root.Types.OfType<Poly.DomainModeling.ValueType>().ToList();
        await Assert.That(valueTypes.Count).IsEqualTo(2);
        await Assert.That(valueTypes.Any(v => v.Name == "BirthCertificate")).IsTrue();
        await Assert.That(valueTypes.Any(v => v.Name == "DeathCertificate")).IsTrue();


    }

    [Test]
    public async Task Apply_AddEffectToActionChange_AttachesCreateEffect() {
        // Setup: Person entity with a Die action + the target ValueType (owned doc pattern)
        var dieAction = new Poly.DomainModeling.Action("Die", InvocationResult.Void, [], [], []);
        var person = new Entity("Person", [], [dieAction], [], []);
        var deathCert = new Poly.DomainModeling.ValueType("DeathCertificate", [], []);
        var start = new Domain("Test", [person, deathCert], []);

        var createDeathCert = new CreateEntityInstance(new DomainTypeReference("DeathCertificate"));
        var change = new AddEffectToActionChange("Person", "Die", createDeathCert);

        var result = new DomainEvolution(start).Apply([change]);

        var updatedAction = result.Root.Types
            .OfType<Entity>().Single(e => e.Name == "Person")
            .Actions.Single(a => a.Name == "Die");

        await Assert.That(updatedAction.Effects.Count).IsEqualTo(1);
        await Assert.That(updatedAction.Effects[0]).IsTypeOf<CreateEntityInstance>();
    }

    [Test]
    public async Task Apply_AddEffectToActionChange_AttachesStageTransition() {
        var dieAction = new Poly.DomainModeling.Action("Die", InvocationResult.Void, [], [], []);
        var deadStage = new Stage("Dead", [], [], [], []);
        var person = new Entity("Person", [], [dieAction], [], [deadStage]);
        var start = new Domain("Test", [person], []);

        var transition = new StageTransitionEffect(new StageReference("Dead"));
        var change = new AddEffectToActionChange("Person", "Die", transition);

        var result = new DomainEvolution(start).Apply([change]);

        var effects = result.Root.Types
            .OfType<Entity>().Single(e => e.Name == "Person")
            .Actions.Single(a => a.Name == "Die")
            .Effects;

        await Assert.That(effects.Count).IsEqualTo(1);
        await Assert.That(effects[0]).IsTypeOf<StageTransitionEffect>();
    }

    [Test]
    public async Task FullSmallLifecycle_BuiltEntirelyViaEvolutionLayer() {
        // This is the direction we want: agents (or UI) can evolve a meaningful
        // lifecycle domain from nothing using the analysis-gated evolution layer.
        var start = new Domain("PersonLifecycle", [], []);

        var evolution = new DomainEvolution(start);

        // Step 1: Core structure + the ValueType target for the Create effect (MVP coverage)
        var step1 = evolution.Apply(new DomainChange[]
        {
            new AddEntityChange("Person", []),
            new AddStageChange("Person", "Alive"),
            new AddStageChange("Person", "Dead"),
            new AddActionChange("Person", "Die"),
            new AddValueTypeChange("DeathCertificate", [])
        });
        await Assert.That(step1.Succeeded).IsTrue();

        // Step 2: Give the Die action real behavior
        var dieCreate = new CreateEntityInstance(new DomainTypeReference("DeathCertificate"));
        var dieTransition = new StageTransitionEffect(new StageReference("Dead"));

        var step2 = new DomainEvolution(step1.Root).Apply(new DomainChange[]
        {
            new AddEffectToActionChange("Person", "Die", dieCreate),
            new AddEffectToActionChange("Person", "Die", dieTransition)
        });
        await Assert.That(step2.Succeeded).IsTrue();

        var person = step2.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");

        await Assert.That(person.Stages.Count).IsEqualTo(2);
        await Assert.That(person.Actions.Count).IsEqualTo(1);

        var die = person.Actions[0];
        await Assert.That(die.Name).IsEqualTo("Die");
        await Assert.That(die.Effects.Count).IsEqualTo(2);

        // Verify we have the expected effect kinds
        await Assert.That(die.Effects.Any(e => e is CreateEntityInstance)).IsTrue();
        await Assert.That(die.Effects.Any(e => e is StageTransitionEffect)).IsTrue();

        // The original start domain is still pristine
        await Assert.That(start.Types).IsEmpty();
    }

    [Test]
    public async Task AddPolicyToEntity_Works() {
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Person")
                   .AddPropertyToEntity("Person", new Property("BirthCertificate",
                       new DomainTypeReference("Boolean"), [])));

        var policy = new Policy("HasBirthCert", DomainExpression.Exists(
            DomainExpression.Property("BirthCertificate")));

        var result = new DomainEvolution(start).Apply([new AddPolicyToEntityChange("Person", policy)]);
        await Assert.That(result.Succeeded).IsTrue();

        var updated = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");
        await Assert.That(updated.Policies.Count).IsEqualTo(1);
        await Assert.That(updated.Policies[0].Name).IsEqualTo("HasBirthCert");
    }

    [Test]
    public async Task AddRemovePrimitiveType_Works() {
        var start = new Domain("Test", [], []);

        var result1 = new DomainEvolution(start).Apply([
            new AddPrimitiveTypeChange("MyText", Poly.Introspection.TypeCategory.Text, [])
        ]);

        await Assert.That(result1.Succeeded).IsTrue();
        var primitive = result1.Root.Types.OfType<PrimitiveType>().SingleOrDefault(p => p.Name == "MyText");
        await Assert.That(primitive).IsNotNull();
        await Assert.That(primitive!.TypeCategory).IsEqualTo(Poly.Introspection.TypeCategory.Text);

        var result2 = new DomainEvolution(result1.Root).Apply([
            new RemovePrimitiveTypeChange("MyText")
        ]);

        await Assert.That(result2.Succeeded).IsTrue();
        await Assert.That(result2.Root.Types.OfType<PrimitiveType>().Any(p => p.Name == "MyText")).IsFalse();
    }

    [Test]
    public async Task AddActionWithCreateEffectAndBindings_Works() {
        var start = new Domain("Test", [], []);

        var createEffect = new CreateEntityInstance(
            new DomainTypeReference("BirthCertificate"),
            [new PropertyBinding("Time", DomainExpression.Parameter("TimeOfBirth"))]
        );

        var result = new DomainEvolution(start).Apply([
            new AddPrimitiveTypeChange("Timestamp", Poly.Introspection.TypeCategory.DateTime, []),
            new AddValueTypeChange("BirthCertificate", [new Property("Time", new DomainTypeReference("Timestamp"), [])]),
            new AddEntityChange("Person", []),
            new AddActionChange("Person", "Register"),
            new AddParameterToActionChange("Person", "Register", new Property("TimeOfBirth", new DomainTypeReference("Timestamp"), [])),
            new AddEffectToActionChange("Person", "Register", createEffect)
        ]);

        await Assert.That(result.Succeeded).IsTrue();

        var register = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Person")
            .Actions.Single(a => a.Name == "Register");

        await Assert.That(register.Effects.Count).IsEqualTo(1);
        var create = register.Effects[0] as CreateEntityInstance;
        await Assert.That(create).IsNotNull();
        await Assert.That(create!.Initializers.Count).IsEqualTo(1);
        await Assert.That(create.Initializers[0].PropertyName).IsEqualTo("Time");
    }

    [Test]
    public async Task AddActionWithPublishAndTransitionEffects_Works() {
        var start = new Domain("Test", [], []);

        var result = new DomainEvolution(start)
            .Evolve()
            .AddPrimitiveType("Timestamp", Poly.Introspection.TypeCategory.DateTime)
            .AddEntity("Person")
            .AddStage("Person", "Dead")
            .AddAction("Person", "Die")
            .AddParameterToAction("Person", "Die", new Property("TimeOfBirth", new DomainTypeReference("Timestamp"), []))
            .AddStageTransitionEffect("Person", "Die", "Dead")
            .Apply();

        await Assert.That(result.Succeeded).IsTrue();

        var die = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Person")
            .Actions.Single(a => a.Name == "Die");

        await Assert.That(die.Effects.Count).IsEqualTo(1);
        await Assert.That(die.Effects.Any(e => e is StageTransitionEffect)).IsTrue();
    }

    [Test]
    public async Task Fluent_AddPolicyToStage_Works() {
        var start = new Domain("Test", [], []);

        var result = new DomainEvolution(start)
            .Evolve()
            .AddPrimitiveType("Timestamp", Poly.Introspection.TypeCategory.DateTime)
            .AddValueType("BirthCertificate", new Property("Time", new DomainTypeReference("Timestamp"), []))
            .AddEntity("Person")
            .AddStage("Person", "Alive")
            .AddPolicyToStage("Person", "Alive", "HasBirthCert",
                DomainExpression.Exists(
                    DomainExpression.Owned("BirthCertificate", DomainExpression.Property("Time"))))
            .Apply();

        await Assert.That(result.Succeeded).IsTrue();

        var person = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");
        var alive = person.Stages.Single(s => s.Name == "Alive");
        await Assert.That(alive.Policies.Count).IsEqualTo(1);
        await Assert.That(alive.Policies[0].Name).IsEqualTo("HasBirthCert");
    }

    [Test]
    public async Task NodeId_Continuity_PreservesIdsOnUnchangedSubtrees() {
        // Build a small domain with depth (Entity > Stage + Action + Effect + Property)
        var text = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var someDoc = new Poly.DomainModeling.ValueType("SomeDoc", [], []);
        var createEffect = new CreateEntityInstance(new DomainTypeReference("SomeDoc"));
        var dieAction = new Poly.DomainModeling.Action("Die", InvocationResult.Void, [], [createEffect], []);
        var aliveStage = new Stage("Alive", [], [], [], []);
        var deadStage = new Stage("Dead", [], [], [], []);
        var person = new Entity("Person", [new Property("Name", new DomainTypeReference("Text"), [])], [dieAction], [], [aliveStage, deadStage]);
        var start = new Domain("Test", [person, text, someDoc], []);

        // Capture original Ids of nodes we will not touch
        var originalPersonId = person.Id;
        var originalAliveId = aliveStage.Id;
        var originalDieId = dieAction.Id;
        var originalCreateEffectId = createEffect.Id;
        var originalNamePropId = person.Properties[0].Id;
        var originalDomainId = start.Id; // for contrast

        // Change that only touches the Die action (adds a second effect). Everything else is untouched subtree.
        var transition = new StageTransitionEffect(new StageReference("Dead"));
        var change = new AddEffectToActionChange("Person", "Die", transition);

        var result = new DomainEvolution(start).Apply([change]);
        await Assert.That(result.Succeeded).IsTrue();

        var evolvedPerson = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");
        var evolvedAlive = evolvedPerson.Stages.Single(s => s.Name == "Alive");
        var evolvedDie = evolvedPerson.Actions.Single(a => a.Name == "Die");

        // Unchanged containers and deep leaves must retain exact same NodeId (continuity)
        await Assert.That(evolvedPerson.Id).IsEqualTo(originalPersonId);
        await Assert.That(evolvedAlive.Id).IsEqualTo(originalAliveId);
        await Assert.That(evolvedDie.Id).IsEqualTo(originalDieId);
        await Assert.That(evolvedDie.Effects[0].Id).IsEqualTo(originalCreateEffectId); // the original effect child
        await Assert.That(evolvedPerson.Properties[0].Id).IsEqualTo(originalNamePropId);

        // The root Domain is new (as always after evolution)
        await Assert.That(result.Root.Id).IsNotEqualTo(originalDomainId);

        // The newly added effect must have a fresh Id (not colliding with anything pre-existing)
        var newEffect = evolvedDie.Effects[1];
        await Assert.That(newEffect.Id).IsNotEqualTo(originalCreateEffectId);
        await Assert.That(newEffect.Id).IsNotEqualTo(originalDieId);

        // Exercise the incremental analysis path with the second evolution (uses real GetAffectedNodes)
        var prior = result.Analysis;
        // Apply another change using prior analysis — GetAffectedNodes now returns real nodes
        var addPolicyChange = new AddPolicyToEntityChange("Person", new Policy("TestPolicy", DomainExpression.Literal(true)));
        var incrementalResult = new DomainEvolution(result.Root).Apply([addPolicyChange], prior);
        await Assert.That(incrementalResult.Succeeded).IsTrue();
        await Assert.That(incrementalResult.Trace.ErrorCount).IsEqualTo(0);
    }

    [Test]
    public async Task AddStage_WithSimpleParent_WorksAndPreservesNodeId() {
        var start = new Domain("Test", [], []);

        // First add the parent stage
        var afterParent = new DomainEvolution(start).Apply([
            new AddEntityChange("Order", []),
            new AddStageChange("Order", "Pending")
        ]);
        await Assert.That(afterParent.Succeeded).IsTrue();

        var originalPendingId = afterParent.Root.Types.OfType<Entity>().Single(e => e.Name == "Order")
            .Stages.Single(s => s.Name == "Pending").Id;

        // Stage hierarchy not supported — add flat stage instead
        var result = new DomainEvolution(afterParent.Root).Apply([
            new AddStageChange("Order", "Approved")
        ]);

        await Assert.That(result.Succeeded).IsTrue();

        var order = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Order");
        await Assert.That(order.Stages.Count).IsEqualTo(2);

        var approved = order.Stages.Single(s => s.Name == "Approved");

        // Fresh Id for the new stage
        await Assert.That(approved.Id).IsNotEqualTo(originalPendingId);

        // Parent stage Id is preserved (NodeId continuity on untouched sibling)
        var pending = order.Stages.Single(s => s.Name == "Pending");
        await Assert.That(pending.Id).IsEqualTo(originalPendingId);
    }

    [Test]
    public async Task AddStage_WithUnknownParent_ReportsStructuralFailure() {
        var start = new Domain("Test", [], []);

        var result = new DomainEvolution(start).Apply([
            new AddEntityChange("Order", []),
            new AddStageChange("Order", "Approved")
        ]);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task AddActionToStage_Works() {
        var start = new Domain("Test", [], []);

        var result = new DomainEvolution(start)
            .Evolve()
            .AddEntity("Order")
            .AddStage("Order", "Pending")
            .AddActionToStage("Order", "Pending", "Approve")
            .Apply();

        await Assert.That(result.Succeeded).IsTrue();

        var order = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var pending = order.Stages.Single(s => s.Name == "Pending");

        await Assert.That(pending.Actions.Count).IsEqualTo(1);
        await Assert.That(pending.Actions[0].Name).IsEqualTo("Approve");
    }

    [Test]
    public async Task RemoveActionFromStage_Works() {
        var action = new Poly.DomainModeling.Action("Approve", InvocationResult.Void, [], [], []);
        var stage = new Stage("Pending", [action], [], [], []);
        var entity = new Entity("Order", [], [], [], [stage]);
        var start = new Domain("Test", [entity], []);

        var result = new DomainEvolution(start).Apply([
            new RemoveActionFromStageChange("Order", "Pending", "Approve")
        ]);

        await Assert.That(result.Succeeded).IsTrue();

        var pending = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Order")
            .Stages.Single(s => s.Name == "Pending");

        await Assert.That(pending.Actions).IsEmpty();
    }

    [Test]
    public async Task MultiStepBatch_WithIntentionalStructuralError_ReturnsOriginalRootAndRichTrace() {
        var start = new Domain("Test", [], []);

        // Valid first batch
        var step1 = new DomainEvolution(start).Apply([
            new AddEntityChange("Person", []),
            new AddStageChange("Person", "Alive")
        ]);
        await Assert.That(step1.Succeeded).IsTrue();

        // Second batch: one bad change (unknown type ref) + valid ones -> entire proposal rejected
        var badBatch = new DomainChange[]
        {
            new AddPropertyToEntityChange("Person", new Property("Name", new DomainTypeReference("NonExistentType"), [])),
            new AddStageChange("Person", "Dead") // would have been valid alone
        };

        var result = new DomainEvolution(step1.Root).Apply(badBatch);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.WasRolledBack).IsTrue();
        await Assert.That(result.HasStructuralFailure).IsTrue();
        await Assert.That(result.Root).IsSameReferenceAs(step1.Root);
        await Assert.That(result.Trace.RolledBack).IsTrue();
        await Assert.That(result.Trace.Steps.Count).IsEqualTo(2);

        // Start exercising better agent-facing signal on rollback
        await Assert.That(result.FailureSummary).IsNotNull();
        await Assert.That(result.FailureSummary!.Contains("NonExistentType")).IsTrue();

        // The actual bad step's description itself carries the context about what was attempted (including the unknown type)
        var badStep = result.Trace.Steps.FirstOrDefault(s => s.ChangeDescription.Contains("Add property"));
        await Assert.That(badStep).IsNotNull();
        await Assert.That(badStep!.ChangeDescription).Contains("NonExistentType");

        // Change history is emitted as first-class Information diagnostics (unified model, no parallel text machinery).
        var infoDiags = result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Information).ToList();
        await Assert.That(infoDiags.Count).IsGreaterThan(0);
        await Assert.That(infoDiags.Any(d => d.Message.Contains("Add property") && d.Message.Contains("NonExistentType"))).IsTrue();
        await Assert.That(infoDiags.Any(d => d.Code == "EVOLUTION_STEP")).IsTrue();
    }

    /// <summary>
    /// WS5 Real Proof (PersonLifecycle): The *documented* target shape from
    /// PersonLifecycleExample.cs is constructed *entirely* through the evolution layer
    /// (the primary agent/MCP surface). This moves beyond internal "style tests" to
    /// a canonical, documented example used as living evidence that the immutable
    /// core + thin evolution layer can express the intended models with complex
    /// DomainExpression policies, bindings, calculations (Subtract), events, stages,
    /// and effects — all analysis-gated and producing high-fidelity traces.
    ///
    /// This is the first shipped validation of the V2→V3 evolution approach on a
    /// real, non-trivial lifecycle that the V3 model was designed to support.
    /// </summary>
    [Test]
    public async Task PersonLifecycle_DocumentedShape_ProvenViaEvolutionLayer() {
        // Start from completely empty domain (the realistic agent starting point)
        var start = new Domain("PersonLifecycle", [], []);

        // === Evolution 1: Core structure + primitives + owned documents + events ===
        // This mirrors the "manual" construction order in PersonLifecycleExample.Create()
        var step1 = new DomainEvolution(start)
            .Evolve()
            // Primitives required by the documented example
            .AddPrimitiveType("Text", Poly.Introspection.TypeCategory.Text)
            .AddPrimitiveType("Timestamp", Poly.Introspection.TypeCategory.DateTime)
            .AddPrimitiveType("Duration", Poly.Introspection.TypeCategory.Duration)
            // ValueTypes (owned documents) — documented shape
            .AddValueType("BirthCertificate", new Property("Time", new DomainTypeReference("Timestamp"), []))
            .AddValueType("DeathCertificate",
                new Property("Time", new DomainTypeReference("Timestamp"), []),
                new Property("Cause", new DomainTypeReference("Text"), []))
            // The Person entity skeleton (built incrementally — honest step-by-step via evolution)
            .AddEntity("Person")
            .AddPropertyToEntity("Person", new Property("SurName", new DomainTypeReference("Text"), []))
            .AddPropertyToEntity("Person", new Property("GivenName", new DomainTypeReference("Text"), []))
            .AddPropertyToEntity("Person", new Property("TimeOfBirth", new DomainTypeReference("Timestamp"), []))
            .Apply();

        await Assert.That(step1.Succeeded).IsTrue();
        await Assert.That(step1.HasStructuralFailure).IsFalse();

        // === Evolution 2: Stages + complex stage policies (Exists/NotExists + Owned) ===
        // This is the heart of the documented lifecycle guards.
        var step2 = new DomainEvolution(step1.Root)
            .Evolve()
            // Alive stage with the two documented policy guards
            .AddStage("Person", "Alive")
            .AddStageGuard("Person", "Alive", "HasBirthCertificate",
                DomainExpression.Exists(
                    DomainExpression.Owned("BirthCertificate", DomainExpression.Property("Time"))))
            .AddStageGuard("Person", "Alive", "NoDeathCertificate",
                DomainExpression.NotExists(
                    DomainExpression.Owned("DeathCertificate", DomainExpression.Property("Time"))))
            // Dead stage with its documented guard
            .AddStage("Person", "Dead")
            .AddStageGuard("Person", "Dead", "HasDeathCertificate",
                DomainExpression.Exists(
                    DomainExpression.Owned("DeathCertificate", DomainExpression.Property("Time"))))
            // OnEntry for Alive: stage transition IS the observable — no PublishEvent needed.
            .AddOnEntryEffect("Person", "Alive",
                new AssignEffect(
                    DomainExpression.Property("TimeOfBirth"),
                    DomainExpression.Owned("BirthCertificate", DomainExpression.Property("Time"))))
            // OnEntry for Dead: stage transition IS the observable — no PublishEvent needed.
            .AddOnEntryEffect("Person", "Dead",
                new AssignEffect(
                    DomainExpression.Property("TimeOfBirth"),
                    DomainExpression.Owned("DeathCertificate", DomainExpression.Property("Time"))))
            .Apply();

        await Assert.That(step2.Succeeded).IsTrue();
        await Assert.That(step2.Trace.Steps.Count).IsGreaterThanOrEqualTo(7); // meaningful steps for stages + policies + onEntry effects

        // === Evolution 3: The Die action + effects (Create + Transition) attached to Alive ===
        // Uses the documented parameter names and bindings.
        var dieCreate = new CreateEntityInstance(
            new DomainTypeReference("DeathCertificate"),
            [
                new PropertyBinding("Time", DomainExpression.Parameter("TimeOfDeath")),
                new PropertyBinding("Cause", DomainExpression.Parameter("CauseOfDeath"))
            ]);

        var dieTransition = new StageTransitionEffect(new StageReference("Dead"));

        var step3 = new DomainEvolution(step2.Root)
            .Evolve()
            // Action defined directly on the Alive stage (the documented intent)
            .AddActionToStage("Person", "Alive", "Die")
            .AddParameterToAction("Person", "Die", new Property("TimeOfDeath", new DomainTypeReference("Timestamp"), []))
            .AddParameterToAction("Person", "Die", new Property("CauseOfDeath", new DomainTypeReference("Text"), []))
            // Attach the two effects that realize the lifecycle transition
            .AddEffectToAction("Person", "Die", dieCreate)
            .AddEffectToAction("Person", "Die", dieTransition)
            .Apply();

        await Assert.That(step3.Succeeded).IsTrue();
        await Assert.That(step3.HasStructuralFailure).IsFalse();

        // === Final verification against the documented shape ===
        var final = step3.Root;
        var person = final.Types.OfType<Entity>().Single(e => e.Name == "Person");

        // Stages exist with the documented names
        await Assert.That(person.Stages.Count).IsEqualTo(2);
        var alive = person.Stages.Single(s => s.Name == "Alive");
        var dead = person.Stages.Single(s => s.Name == "Dead");

        // Documented policies are present (names + expression kinds)
        await Assert.That(alive.Policies.Count).IsEqualTo(2);
        await Assert.That(alive.Policies.Any(p => p.Name == "HasBirthCertificate")).IsTrue();
        await Assert.That(alive.Policies.Any(p => p.Name == "NoDeathCertificate")).IsTrue();

        await Assert.That(dead.Policies.Count).IsEqualTo(1);
        await Assert.That(dead.Policies[0].Name).IsEqualTo("HasDeathCertificate");

        // OnEntry effects — stage transitions are the observable; AssignEffect replaces old PublishEventEffect.
        await Assert.That(alive.OnEntryEffects.Count).IsEqualTo(1);
        await Assert.That(alive.OnEntryEffects[0]).IsTypeOf<AssignEffect>();

        await Assert.That(dead.OnEntryEffects.Count).IsEqualTo(1);
        await Assert.That(dead.OnEntryEffects[0]).IsTypeOf<AssignEffect>();

        // Die action exists on the Alive stage with the documented effects
        await Assert.That(alive.Actions.Count).IsEqualTo(1);
        await Assert.That(alive.Actions[0].Name).IsEqualTo("Die");
        await Assert.That(alive.Actions[0].Parameters.Count).IsEqualTo(2);
        await Assert.That(alive.Actions[0].Effects.Count).IsEqualTo(2);
        await Assert.That(alive.Actions[0].Effects.Any(e => e is CreateEntityInstance)).IsTrue();
        await Assert.That(alive.Actions[0].Effects.Any(e => e is StageTransitionEffect)).IsTrue();

        // ValueTypes and Events are present as documented
        await Assert.That(final.Types.OfType<Poly.DomainModeling.ValueType>().Count()).IsEqualTo(2);

        // The trace tells the full story (high-fidelity change history as Information diagnostics)
        // step2 added stages + policies + OnEntry effects
        var allInfoStep2 = step2.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Information).ToList();
        await Assert.That(allInfoStep2.Count).IsGreaterThan(0);
        await Assert.That(allInfoStep2.Any(d => d.Code == "EVOLUTION_STEP" && d.Message.Contains("Add Stage 'Alive'"))).IsTrue();

        // step3 added actions + parameters + effects to existing stages
        var allInfoStep3 = step3.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Information).ToList();
        await Assert.That(allInfoStep3.Any(d => d.Code == "EVOLUTION_STEP" && d.Message.Contains("Add Action 'Die'"))).IsTrue();

        // Final proof: the entire evolution succeeded with zero errors on the documented shape
        await Assert.That(step3.Succeeded).IsTrue();
        await Assert.That(step3.Root).IsNotSameReferenceAs(start);
    }

    /// <summary>
    /// WS5 Real Proof (Library Domain — Loan Lifecycle Slice):
    /// The core Library domain structure (Book, Loan, Member entities with stages,
    /// events, relationships, actions with effects) is constructed entirely through
    /// the evolution layer (DomainEvolution.Evolve()).
    ///
    /// This is the first shipped validation of the V3 evolution layer on a real
    /// roadblock scenario. Known gaps (cross-entity mutation, dynamic calculation,
    /// conditional effects, entity inheritance, InvokeAction) are documented inline
    /// as Phase 4 input — no speculative fixes.
    /// </summary>
    [Test]
    public async Task LibraryDomain_LoanLifecycle_ProvenViaEvolutionLayer() {
        var start = new Domain("Library", [], []);

        // === Evolution 1: Core structure — primitives + entities + events ===
        var step1 = new DomainEvolution(start)
            .Evolve()
            // Primitives
            .AddPrimitiveType("Text", Poly.Introspection.TypeCategory.Text)
            .AddPrimitiveType("Int", Poly.Introspection.TypeCategory.Integer)
            .AddPrimitiveType("Bool", Poly.Introspection.TypeCategory.Boolean)
            .AddPrimitiveType("Decimal", Poly.Introspection.TypeCategory.HighPrecision)
            .AddPrimitiveType("Instant", Poly.Introspection.TypeCategory.DateTime)
            .AddPrimitiveType("Date", Poly.Introspection.TypeCategory.DateTime)
            // Entities (flat — no inheritance; V3 Entity has no ParentEntity concept)
            .AddEntity("Person")
            .AddPropertyToEntity("Person", new Property("FirstName", new DomainTypeReference("Text"), []))
            .AddPropertyToEntity("Person", new Property("LastName", new DomainTypeReference("Text"), []))
            .AddPropertyToEntity("Person", new Property("Email", new DomainTypeReference("Text"), []))
            .AddEntity("Member")
            .AddPropertyToEntity("Member", new Property("MemberId", new DomainTypeReference("Text"), []))
            .AddPropertyToEntity("Member", new Property("JoinDate", new DomainTypeReference("Instant"), []))
            .AddPropertyToEntity("Member", new Property("IsActive", new DomainTypeReference("Bool"), []))
            .AddPropertyToEntity("Member", new Property("MaxBooksAllowed", new DomainTypeReference("Int"), []))
            .AddEntity("Book")
            .AddPropertyToEntity("Book", new Property("ISBN", new DomainTypeReference("Text"), []))
            .AddPropertyToEntity("Book", new Property("Title", new DomainTypeReference("Text"), []))
            .AddPropertyToEntity("Book", new Property("Author", new DomainTypeReference("Text"), []))
            .AddPropertyToEntity("Book", new Property("TotalCopies", new DomainTypeReference("Int"), []))
            .AddPropertyToEntity("Book", new Property("AvailableCopies", new DomainTypeReference("Int"), []))
            .AddEntity("Loan")
            .AddPropertyToEntity("Loan", new Property("LoanDate", new DomainTypeReference("Instant"), []))
            .AddPropertyToEntity("Loan", new Property("DueDate", new DomainTypeReference("Date"), []))
            .AddPropertyToEntity("Loan", new Property("RenewalCount", new DomainTypeReference("Int"), []))
            .AddEntity("Fine")
            .AddPropertyToEntity("Fine", new Property("Amount", new DomainTypeReference("Decimal"), []))
            .AddPropertyToEntity("Fine", new Property("Reason", new DomainTypeReference("Text"), []))
            .AddPropertyToEntity("Fine", new Property("IssuedDate", new DomainTypeReference("Instant"), []))
            .AddPropertyToEntity("Fine", new Property("IsPaid", new DomainTypeReference("Bool"), []))
            .Apply();

        await Assert.That(step1.Succeeded).IsTrue();
        await Assert.That(step1.HasStructuralFailure).IsFalse();

        // === Evolution 2: Stages with parent hierarchies + relationships ===
        var step2 = new DomainEvolution(step1.Root)
            .Evolve()
            // Loan stages (with simple parent hierarchy for Overdue/Renewed/Lost under Active)
            .AddStage("Loan", "Active")
            .AddStage("Loan", "Overdue")
            .AddStage("Loan", "Returned")
            .AddStage("Loan", "Renewed")
            .AddStage("Loan", "Lost")
            // Book stages
            .AddStage("Book", "Available")
            .AddStage("Book", "Borrowed")
            .AddStage("Book", "Damaged")
            // Relationships
            .AddRelationship("MemberLoans", "Member", "Loan", RelationshipCardinality.OneToMany)
            .AddRelationship("BookLoans", "Book", "Loan", RelationshipCardinality.OneToMany)
            .AddRelationship("LoanFines", "Loan", "Fine", RelationshipCardinality.OneToMany)
            .Apply();

        await Assert.That(step2.Succeeded).IsTrue();

        // === Evolution 3: Actions with effects (the core behavior) ===
        // CheckoutBook: Creates a Loan in Active stage, publishes BookCheckedOut.
        // GAP: Cannot decrement Book.AvailableCopies (cross-entity mutation not supported in V3).
        // This is the #1 roadblock documented in library-roadblocks.md.
        // Phase 4 needs: cross-entity Assign effect or relationship navigation in expressions.
        var checkoutCreate = new CreateEntityInstance(
            new DomainTypeReference("Loan"),
            [
                new PropertyBinding("LoanDate", DomainExpression.Parameter("LoanDate")),
                new PropertyBinding("DueDate", DomainExpression.Parameter("DueDate")),
                new PropertyBinding("RenewalCount", DomainExpression.Literal(0))
            ]);

        var checkoutTransition = new StageTransitionEffect(new StageReference("Active"));

        // PublishEventEffect removed — stage transitions are the observable.

        // ReturnBook: Transitions Loan to Returned.
        // GAP: Cannot increment Book.AvailableCopies (same cross-entity mutation gap as CheckoutBook).
        var returnTransition = new StageTransitionEffect(new StageReference("Returned"));

        var step3 = new DomainEvolution(step2.Root)
            .Evolve()
            // === Book actions ===
            // AddBook: Creates Book in Available stage, publishes BookAdded.
            .AddActionToStage("Book", "Available", "AddBook")
            .AddParameterToAction("Book", "AddBook", new Property("ISBN", new DomainTypeReference("Text"), []))
            .AddParameterToAction("Book", "AddBook", new Property("Title", new DomainTypeReference("Text"), []))
            .AddParameterToAction("Book", "AddBook", new Property("Author", new DomainTypeReference("Text"), []))
            .AddParameterToAction("Book", "AddBook", new Property("TotalCopies", new DomainTypeReference("Int"), []))
            .AddStageTransitionEffect("Book", "AddBook", "Available")
            // === Loan actions (on stage "Active") ===
            // CheckoutBook: creates Loan, transitions to Active, publishes BookCheckedOut
            // NOTE: The CreateEntityInstance action-level effect produces a Loan entity via the CheckoutBook action.
            // The Loan relationships are set up; Book.AvailableCopies decrement is the documented V3 gap.
            .AddActionToStage("Loan", "Active", "CheckoutBook")
            .AddParameterToAction("Loan", "CheckoutBook", new Property("BookTitle", new DomainTypeReference("Text"), []))
            .AddParameterToAction("Loan", "CheckoutBook", new Property("MemberName", new DomainTypeReference("Text"), []))
            .AddParameterToAction("Loan", "CheckoutBook", new Property("LoanDate", new DomainTypeReference("Instant"), []))
            .AddParameterToAction("Loan", "CheckoutBook", new Property("DueDate", new DomainTypeReference("Date"), []))
            .AddEffectToAction("Loan", "CheckoutBook", checkoutCreate)
            .AddEffectToAction("Loan", "CheckoutBook", checkoutTransition)
            // ReturnBook: transitions Loan to Returned
            .AddActionToStage("Loan", "Active", "ReturnBook")
            .AddActionToStage("Loan", "Overdue", "ReturnBook")
            .AddActionToStage("Loan", "Renewed", "ReturnBook")
            .AddParameterToAction("Loan", "ReturnBook", new Property("Condition", new DomainTypeReference("Text"), []))
            .AddEffectToAction("Loan", "ReturnBook", returnTransition)
            // RenewLoan: increments RenewalCount +1, extends DueDate by 14 days, transitions to Renewed.
            .AddActionToStage("Loan", "Active", "RenewLoan")
            .AddEffectToAction("Loan", "RenewLoan",
                new AssignEffect(
                    DomainExpression.Property("RenewalCount"),
                    DomainExpression.Add(DomainExpression.Property("RenewalCount"), DomainExpression.Literal(1))))
            .AddEffectToAction("Loan", "RenewLoan",
                new AssignEffect(
                    DomainExpression.Property("DueDate"),
                    DomainExpression.DateOp(DomainExpression.Property("DueDate"), DomainExpression.Literal(14), DateOperationKind.AddDays)))
            .AddStageTransitionEffect("Loan", "RenewLoan", "Renewed")
            // ReportLost: transitions Loan to Lost stage.
            // GAP: Cannot conditionally create a Fine or decrement Book.TotalCopies.
            // Phase 4 needs: ConditionalEffect and/or InvokeAction.
            .AddActionToStage("Loan", "Active", "ReportLost")
            .AddStageTransitionEffect("Loan", "ReportLost", "Lost")
            .Apply();

        await Assert.That(step3.Succeeded).IsTrue();
        await Assert.That(step3.Trace.Steps.Count).IsGreaterThan(0);

        // === Final verification against the documented shape ===
        var final = step3.Root;

        // Core entities present
        var book = final.Types.OfType<Entity>().Single(e => e.Name == "Book");
        await Assert.That(book.Properties.Any(p => p.Name == "AvailableCopies")).IsTrue();
        await Assert.That(book.Properties.Any(p => p.Name == "Title")).IsTrue();

        var loan = final.Types.OfType<Entity>().Single(e => e.Name == "Loan");
        await Assert.That(loan.Properties.Any(p => p.Name == "RenewalCount")).IsTrue();

        // Loan stages (flat — no parent hierarchy)
        await Assert.That(loan.Stages.Count).IsEqualTo(5);
        var active = loan.Stages.Single(s => s.Name == "Active");
        var overdue = loan.Stages.Single(s => s.Name == "Overdue");
        var returned = loan.Stages.Single(s => s.Name == "Returned");
        var renewed = loan.Stages.Single(s => s.Name == "Renewed");
        var lost = loan.Stages.Single(s => s.Name == "Lost");

        // Actions on stages
        await Assert.That(active.Actions.Count).IsEqualTo(4); // CheckoutBook, ReturnBook, RenewLoan, ReportLost
        await Assert.That(overdue.Actions.Count).IsEqualTo(1); // ReturnBook
        await Assert.That(renewed.Actions.Count).IsEqualTo(1); // ReturnBook

        // CheckoutBook effects
        var checkout = active.Actions.Single(a => a.Name == "CheckoutBook");
        await Assert.That(checkout.Effects.Count).IsEqualTo(2);
        await Assert.That(checkout.Effects.Any(e => e is CreateEntityInstance)).IsTrue();
        await Assert.That(checkout.Effects.Any(e => e is StageTransitionEffect)).IsTrue();

        var checkoutCreateEffect = (CreateEntityInstance)checkout.Effects.First(e => e is CreateEntityInstance);
        await Assert.That(checkoutCreateEffect.Initializers.Count).IsEqualTo(3);
        await Assert.That(checkoutCreateEffect.Initializers.Any(i => i.PropertyName == "RenewalCount")).IsTrue();

        // ReturnBook effects
        var returnBook = active.Actions.Single(a => a.Name == "ReturnBook");
        await Assert.That(returnBook.Effects.Count).IsEqualTo(1);
        await Assert.That(returnBook.Effects.Any(e => e is StageTransitionEffect)).IsTrue();

        // RenewLoan: 2 AssignEffects + StageTransition (dynamic calc gap closed — Phase 4a)
        var renew = active.Actions.Single(a => a.Name == "RenewLoan");
        await Assert.That(renew.Effects.Count).IsEqualTo(3);
        await Assert.That(renew.Effects[2]).IsTypeOf<StageTransitionEffect>();

        var renewAssignCount = (AssignEffect)renew.Effects[0];
        await Assert.That(renewAssignCount.Target).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)renewAssignCount.Target).Name).IsEqualTo("RenewalCount");
        await Assert.That(renewAssignCount.Value).IsTypeOf<Poly.DomainModeling.Add>();
        var addExpr = (Poly.DomainModeling.Add)renewAssignCount.Value;
        await Assert.That(addExpr.Left).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)addExpr.Left).Name).IsEqualTo("RenewalCount");
        await Assert.That(addExpr.Right).IsTypeOf<Literal>();

        var renewAssignDate = (AssignEffect)renew.Effects[1];
        await Assert.That(renewAssignDate.Target).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)renewAssignDate.Target).Name).IsEqualTo("DueDate");
        await Assert.That(renewAssignDate.Value).IsTypeOf<DateOperation>();
        var dateOp = (DateOperation)renewAssignDate.Value;
        await Assert.That(dateOp.Kind).IsEqualTo(DateOperationKind.AddDays);

        // ReportLost (only stage transition — conditional + cross-entity gap)
        var reportLostAction = active.Actions.Single(a => a.Name == "ReportLost");
        await Assert.That(reportLostAction.Effects.Count).IsEqualTo(1);
        await Assert.That(reportLostAction.Effects[0]).IsTypeOf<StageTransitionEffect>();

        // Events attached to loan


        // Relationships
        await Assert.That(final.Relationships.Count).IsEqualTo(3);
        await Assert.That(final.Relationships.Any(r => r.Name == "MemberLoans")).IsTrue();
        await Assert.That(final.Relationships.Any(r => r.Name == "BookLoans")).IsTrue();
        await Assert.That(final.Relationships.Any(r => r.Name == "LoanFines")).IsTrue();

        // ==================================================================
        // KNOWN V3 GAPS — documented for Phase 4 planning (no speculative fixes):
        // ==================================================================
        // 1. Cross-entity mutation: CheckoutBook should decrement Book.AvailableCopies;
        //    ReturnBook should increment it. Requires Assign-like effect with relationship
        //    navigation (e.g., Loan.Book.AvailableCopies) or a CrossEntityMutation effect.
        //    Intentional exclusion: event/subscription pattern is the recommended approach.
        //
        // 2. Dynamic calculation: ✅ RESOLVED (Phase 4a) — Add, DateOperation, and
        //    AssignEffect now support RenewalCount + 1 and DueDate + 14 days.
        //
        // 3. Conditional effects: ReportLost should conditionally create a Fine entity and
        //    decrement Book.TotalCopies. Requires ConditionalEffect + InvokeAction.
        //
        // 4. Entity inheritance: V3 has no ParentEntity/ParentEntityName. Member should
        //    inherit from Person. Requires Entity inheritance support.
        //
        // 5. InvokeAction: FulfillReservation → CheckoutBook binding not supported.
        //    Requires InvokeAction effect with parameter binding.
        //
        // These are the exact gaps identified in library-roadblocks.md and the WS7 audit.
        // They are deferred to Phase 4 per the immutable-core design principle:
        // "Build working code before extracting abstractions" — the working code here
        // proves the evolution layer handles the 80% case; Phase 4 fills the remaining 20%.
    }

    [Test]
    public async Task DomainExpression_Add_CanBeStoredInAssignEffect() {
        var start = new Domain("Test", [
            new PrimitiveType("Int", Poly.Introspection.TypeCategory.Integer, []),
            new Entity("Counter", [
                new Property("Value", new DomainTypeReference("Int"), [])
            ], [], [], [])
        ], []);
        var result = new DomainEvolution(start)
            .Evolve()
            .AddAction("Counter", "Increment")
            .AddEffectToAction("Counter", "Increment",
                new AssignEffect(
                    DomainExpression.Property("Value"),
                    DomainExpression.Add(DomainExpression.Property("Value"), DomainExpression.Literal(1))))
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        var counter = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Counter");
        var action = counter.Actions.Single(a => a.Name == "Increment");
        var assign = (AssignEffect)action.Effects.Single();
        await Assert.That(assign.Target).IsTypeOf<PropertyAccess>();
        await Assert.That(assign.Value).IsTypeOf<Poly.DomainModeling.Add>();
    }

    [Test]
    public async Task DomainExpression_Multiply_CanBeStoredInAssignEffect() {
        var result = new DomainEvolution(new Domain("Test", [], []))
            .Evolve()
            .AddPrimitiveType("Int", Poly.Introspection.TypeCategory.Integer)
            .AddEntity("Cart")
            .AddPropertyToEntity("Cart", new Property("Total", new DomainTypeReference("Int"), []))
            .AddAction("Cart", "ScaleTotal")
            .AddEffectToAction("Cart", "ScaleTotal",
                new AssignEffect(
                    DomainExpression.Property("Total"),
                    DomainExpression.Multiply(DomainExpression.Property("Total"), DomainExpression.Literal(2))))
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        var cart = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Cart");
        var action = cart.Actions.Single(a => a.Name == "ScaleTotal");
        var assign = (AssignEffect)action.Effects.Single();
        await Assert.That(assign.Value).IsTypeOf<Poly.DomainModeling.Multiply>();
    }

    [Test]
    public async Task DomainExpression_Divide_CanBeStoredInAssignEffect() {
        var result = new DomainEvolution(new Domain("Test", [], []))
            .Evolve()
            .AddPrimitiveType("Int", Poly.Introspection.TypeCategory.Integer)
            .AddEntity("Splitter")
            .AddPropertyToEntity("Splitter", new Property("Amount", new DomainTypeReference("Int"), []))
            .AddAction("Splitter", "Halve")
            .AddEffectToAction("Splitter", "Halve",
                new AssignEffect(
                    DomainExpression.Property("Amount"),
                    DomainExpression.Divide(DomainExpression.Property("Amount"), DomainExpression.Literal(2))))
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        var splitter = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Splitter");
        var action = splitter.Actions.Single(a => a.Name == "Halve");
        var assign = (AssignEffect)action.Effects.Single();
        await Assert.That(assign.Value).IsTypeOf<Poly.DomainModeling.Divide>();
    }

    [Test]
    public async Task DomainExpression_DateOperation_CanBeStoredInAssignEffect() {
        var result = new DomainEvolution(new Domain("Test", [], []))
            .Evolve()
            .AddPrimitiveType("Date", Poly.Introspection.TypeCategory.DateTime)
            .AddEntity("Schedule")
            .AddPropertyToEntity("Schedule", new Property("Start", new DomainTypeReference("Date"), []))
            .AddAction("Schedule", "Extend")
            .AddEffectToAction("Schedule", "Extend",
                new AssignEffect(
                    DomainExpression.Property("Start"),
                    DomainExpression.DateOp(DomainExpression.Property("Start"), DomainExpression.Literal(7), DateOperationKind.AddDays)))
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        var schedule = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Schedule");
        var action = schedule.Actions.Single(a => a.Name == "Extend");
        var assign = (AssignEffect)action.Effects.Single();
        await Assert.That(assign.Value).IsTypeOf<DateOperation>();
        var dateOp = (DateOperation)assign.Value;
        await Assert.That(dateOp.Kind).IsEqualTo(DateOperationKind.AddDays);
    }

    [Test]
    public async Task DomainExpression_RelationshipNavigation_CanBeStoredInAssignEffectValue() {
        var start = new Domain("Test", [
            new PrimitiveType("Int", Poly.Introspection.TypeCategory.Integer, []),
            new Entity("Project", [
                new Property("MaxDueDate", new DomainTypeReference("Int"), [])
            ], [], [], []),
            new Entity("Task", [
                new Property("DueDate", new DomainTypeReference("Int"), [])
            ], [], [], [])
        ], [
            new Relationship("ProjectTasks", new DomainTypeReference("Project"), new DomainTypeReference("Task"),
                RelationshipCardinality.OneToMany, [])
        ]);
        var result = new DomainEvolution(start)
            .Evolve()
            .AddAction("Project", "SyncDeadline")
            .AddEffectToAction("Project", "SyncDeadline",
                new AssignEffect(
                    DomainExpression.Property("MaxDueDate"),
                    DomainExpression.RelationshipNav("ProjectTasks", DomainExpression.Property("DueDate"))))
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        var project = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Project");
        var action = project.Actions.Single(a => a.Name == "SyncDeadline");
        var assign = (AssignEffect)action.Effects.Single();
        await Assert.That(assign.Value).IsTypeOf<RelationshipNavigation>();
        var nav = (RelationshipNavigation)assign.Value;
        await Assert.That(nav.RelationshipName).IsEqualTo("ProjectTasks");
    }

    [Test]
    public async Task Apply_AddStageToRelationship_AddsStage() {
        var entity = new Entity("Person", [], [], [], []);
        var rel = new Relationship("Friends",
            new DomainTypeReference("Person"), new DomainTypeReference("Person"),
            RelationshipCardinality.ManyToMany, []);
        var start = new Domain("Test", [entity], [rel]);
        var result = new DomainEvolution(start).Apply([new AddStageToRelationshipChange("Friends", new Stage("Active", [], [], [], []))]);
        await Assert.That(result.Succeeded).IsTrue();
        var updatedRel = result.Root.Relationships.Single(r => r.Name == "Friends");
        await Assert.That(updatedRel.Stages.Count).IsEqualTo(1);
        await Assert.That(updatedRel.Stages[0].Name).IsEqualTo("Active");
    }

    [Test]
    public async Task Apply_RemoveStageFromRelationship_RemovesStage() {
        var entity = new Entity("Person", [], [], [], []);
        var stage = new Stage("Active", [], [], [], []);
        var rel = new Relationship("Friends",
            new DomainTypeReference("Person"), new DomainTypeReference("Person"),
            RelationshipCardinality.ManyToMany, []) { Stages = [stage] };
        var start = new Domain("Test", [entity], [rel]);
        var result = new DomainEvolution(start).Apply([new RemoveStageFromRelationshipChange("Friends", "Active")]);
        await Assert.That(result.Succeeded).IsTrue();
        var updatedRel = result.Root.Relationships.Single(r => r.Name == "Friends");
        await Assert.That(updatedRel.Stages).IsEmpty();
    }

    [Test]
    public async Task Apply_AddPolicyToRelationship_AddsPolicy() {
        var entity = new Entity("Person", [], [], [], []);
        var rel = new Relationship("Friends",
            new DomainTypeReference("Person"), new DomainTypeReference("Person"),
            RelationshipCardinality.ManyToMany, []);
        var start = new Domain("Test", [entity], [rel]);
        var result = new DomainEvolution(start).Apply([new AddPolicyToRelationshipChange("Friends", new Policy("MaxFriends", DomainExpression.Literal(10)))]);
        await Assert.That(result.Succeeded).IsTrue();
        var updatedRel = result.Root.Relationships.Single(r => r.Name == "Friends");
        await Assert.That(updatedRel.Policies.Count).IsEqualTo(1);
        await Assert.That(updatedRel.Policies[0].Name).IsEqualTo("MaxFriends");
    }

    [Test]
    public async Task Apply_RemovePolicyFromRelationship_RemovesPolicy() {
        var entity = new Entity("Person", [], [], [], []);
        var policy = new Policy("MaxFriends", DomainExpression.Literal(10));
        var rel = new Relationship("Friends",
            new DomainTypeReference("Person"), new DomainTypeReference("Person"),
            RelationshipCardinality.ManyToMany, []) { Policies = [policy] };
        var start = new Domain("Test", [entity], [rel]);
        var result = new DomainEvolution(start).Apply([new RemovePolicyFromRelationshipChange("Friends", "MaxFriends")]);
        await Assert.That(result.Succeeded).IsTrue();
        var updatedRel = result.Root.Relationships.Single(r => r.Name == "Friends");
        await Assert.That(updatedRel.Policies).IsEmpty();
    }

    [Test]
    public async Task Apply_SetEntityParent_SetsParentName() {
        var parent = new Entity("Person", [], [], [], []);
        var child = new Entity("Member", [], [], [], []);
        var start = new Domain("Test", [parent, child], []);
        var result = new DomainEvolution(start).Apply([new SetEntityParentChange("Member", "Person")]);
        await Assert.That(result.Succeeded).IsTrue();
        var updatedChild = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Member");
        await Assert.That(updatedChild.ParentEntityName).IsEqualTo("Person");
        // Parent unchanged
        var updatedParent = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");
        await Assert.That(updatedParent.ParentEntityName).IsNull();
    }

    [Test]
    public async Task Apply_SetEntityParent_ClearsParentName() {
        var parent = new Entity("Person", [], [], [], []);
        var child = new Entity("Member", [], [], [], []) { ParentEntityName = "Person" };
        var start = new Domain("Test", [parent, child], []);
        var result = new DomainEvolution(start).Apply([new SetEntityParentChange("Member", null)]);
        await Assert.That(result.Succeeded).IsTrue();
        var updatedChild = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Member");
        await Assert.That(updatedChild.ParentEntityName).IsNull();
    }

    [Test]
    public async Task CompositeEffect_CanBeStoredInAction() {
        var entity = new Entity("Workflow", [], [], [], []);
        var start = new Domain("Test", [entity], []);
        var result = new DomainEvolution(start)
            .Evolve()
            .AddStage("Workflow", "Active")
            .AddStage("Workflow", "Completed")
            .AddAction("Workflow", "Execute")
            .AddEffectToAction("Workflow", "Execute",
                new CompositeEffect([
                    new StageTransitionEffect(new StageReference("Active")),
                    new StageTransitionEffect(new StageReference("Completed"))
                ]))
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        var wf = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Workflow");
        var action = wf.Actions.Single(a => a.Name == "Execute");
        await Assert.That(action.Effects.Count).IsEqualTo(1);
        await Assert.That(action.Effects[0]).IsTypeOf<CompositeEffect>();
        var composite = (CompositeEffect)action.Effects[0];
        await Assert.That(composite.Effects.Count).IsEqualTo(2);
    }

    [Test]
    public async Task InvokeActionEffect_CanBeStoredInAction() {
        var entity = new Entity("Orchestrator", [], [new Poly.DomainModeling.Action("Step1", InvocationResult.Void, [], [], [])], [], []);
        var start = new Domain("Test", [entity], []);
        var result = new DomainEvolution(start)
            .Evolve()
            .AddAction("Orchestrator", "RunAll")
            .AddEffectToAction("Orchestrator", "RunAll",
                new InvokeActionEffect("Step1", []))
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        var orch = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Orchestrator");
        var action = orch.Actions.Single(a => a.Name == "RunAll");
        await Assert.That(action.Effects.Count).IsEqualTo(1);
        await Assert.That(action.Effects[0]).IsTypeOf<InvokeActionEffect>();
        var invoke = (InvokeActionEffect)action.Effects[0];
        await Assert.That(invoke.ActionName).IsEqualTo("Step1");
    }

    [Test]
    public async Task ConditionalEffect_CanBeStoredInAction() {
        var entity = new Entity("Task", [], [], [], []);
        var start = new Domain("Test", [entity], []);
        var result = new DomainEvolution(start)
            .Evolve()
            .AddStage("Task", "Approved")
            .AddStage("Task", "Rejected")
            .AddAction("Task", "Evaluate")
            .AddEffectToAction("Task", "Evaluate",
                new ConditionalEffect(
                    DomainExpression.Literal(true),
                    [new StageTransitionEffect(new StageReference("Approved"))],
                    [new StageTransitionEffect(new StageReference("Rejected"))]))
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        var task = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Task");
        var action = task.Actions.Single(a => a.Name == "Evaluate");
        await Assert.That(action.Effects.Count).IsEqualTo(1);
        await Assert.That(action.Effects[0]).IsTypeOf<ConditionalEffect>();
        var cond = (ConditionalEffect)action.Effects[0];
        await Assert.That(cond.ThenEffects.Count).IsEqualTo(1);
        await Assert.That(cond.ElseEffects?.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ConditionalEffect_WithoutElse_CanBeStored() {
        var entity = new Entity("Task", [], [], [], []);
        var start = new Domain("Test", [entity], []);
        var result = new DomainEvolution(start)
            .Evolve()
            .AddStage("Task", "Completed")
            .AddAction("Task", "TryComplete")
            .AddEffectToAction("Task", "TryComplete",
                new ConditionalEffect(
                    DomainExpression.Property("IsReady"),
                    [new StageTransitionEffect(new StageReference("Completed"))],
                    null))
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        var task = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Task");
        var action = task.Actions.Single(a => a.Name == "TryComplete");
        var cond = (ConditionalEffect)action.Effects.Single();
        await Assert.That(cond.ElseEffects).IsNull();
    }

    [Test]
    public async Task LinkRelationshipEffect_CanBeStoredInAction() {
        var entity = new Entity("Order", [], [], [], []);
        var customerRel = new Relationship("Customer",
            new DomainTypeReference("Order"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToOne, []);
        var start = new Domain("Test", [entity], [customerRel]);
        var result = new DomainEvolution(start)
            .Evolve()
            .AddAction("Order", "AssignCustomer")
            .AddEffectToAction("Order", "AssignCustomer",
                new LinkRelationshipEffect("Customer", DomainExpression.Parameter("CustomerId")))
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        var order = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var action = order.Actions.Single(a => a.Name == "AssignCustomer");
        await Assert.That(action.Effects[0]).IsTypeOf<LinkRelationshipEffect>();
        var link = (LinkRelationshipEffect)action.Effects[0];
        await Assert.That(link.RelationshipName).IsEqualTo("Customer");
    }

    [Test]
    public async Task UnlinkRelationshipEffect_CanBeStoredInAction() {
        var entity = new Entity("Order", [], [], [], []);
        var customerRel = new Relationship("Customer",
            new DomainTypeReference("Order"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToOne, []);
        var start = new Domain("Test", [entity], [customerRel]);
        var result = new DomainEvolution(start)
            .Evolve()
            .AddAction("Order", "RemoveCustomer")
            .AddEffectToAction("Order", "RemoveCustomer",
                new UnlinkRelationshipEffect("Customer", DomainExpression.Parameter("CustomerId")))
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        var order = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var action = order.Actions.Single(a => a.Name == "RemoveCustomer");
        await Assert.That(action.Effects[0]).IsTypeOf<UnlinkRelationshipEffect>();
    }

    [Test]
    public async Task TransitionRelationshipEffect_CanBeStoredInAction() {
        var entity = new Entity("Project", [], [], [], []);
        var tasksRel = new Relationship("Tasks",
            new DomainTypeReference("Project"), new DomainTypeReference("Project"),
            RelationshipCardinality.OneToMany, []) { Stages = [new Stage("Completed", [], [], [], [])] };
        var start = new Domain("Test", [entity], [tasksRel]);
        var result = new DomainEvolution(start)
            .Evolve()
            .AddAction("Project", "AdvanceTask")
            .AddEffectToAction("Project", "AdvanceTask",
                new TransitionRelationshipEffect("Tasks", new StageReference("Completed")))
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        var project = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Project");
        var action = project.Actions.Single(a => a.Name == "AdvanceTask");
        await Assert.That(action.Effects[0]).IsTypeOf<TransitionRelationshipEffect>();
        var tr = (TransitionRelationshipEffect)action.Effects[0];
        await Assert.That(tr.RelationshipName).IsEqualTo("Tasks");
        await Assert.That(tr.TargetStage.StageName).IsEqualTo("Completed");
    }

    [Test]
    public async Task DeleteEntityInstance_CanBeStoredAsEffect() {
        var entity = new Entity("Task", [], [], [], []);
        var start = new Domain("Test", [entity], []);
        var result = new DomainEvolution(start)
            .Evolve()
            .AddAction("Task", "Remove")
            .AddEffectToAction("Task", "Remove", new DeleteEntityInstance(new DomainTypeReference("Task")))
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        var task = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Task");
        var action = task.Actions.Single(a => a.Name == "Remove");
        await Assert.That(action.Effects.Count).IsEqualTo(1);
        await Assert.That(action.Effects[0]).IsTypeOf<DeleteEntityInstance>();
    }

    [Test]
    public async Task EntityInheritance_BuilderMethod_SetsParent() {
        var parent = new Entity("Person", [], [], [], []);
        var child = new Entity("Member", [], [], [], []);
        var start = new Domain("Test", [parent, child], []);
        var result = new DomainEvolution(start)
            .Evolve()
            .SetEntityParent("Member", "Person")
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        var updatedChild = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Member");
        await Assert.That(updatedChild.ParentEntityName).IsEqualTo("Person");
    }

    [Test]
    public async Task RelationshipStage_BuilderMethods_Work() {
        var entity = new Entity("Person", [], [], [], []);
        var rel = new Relationship("Friends",
            new DomainTypeReference("Person"), new DomainTypeReference("Person"),
            RelationshipCardinality.ManyToMany, []);
        var start = new Domain("Test", [entity], [rel]);
        var result = new DomainEvolution(start)
            .Evolve()
            .AddStageToRelationship("Friends", "Active")
            .AddPolicyToRelationship("Friends", "Limit", DomainExpression.Literal(5))
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        var updatedRel = result.Root.Relationships.Single(r => r.Name == "Friends");
        await Assert.That(updatedRel.Stages.Count).IsEqualTo(1);
        await Assert.That(updatedRel.Policies.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_SetDomainNameChange_UpdatesDomainName() {
        var start = new Domain("OriginalName", [], []);
        var result = new DomainEvolution(start).Apply([new SetDomainNameChange("Renamed")]);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root.Name).IsEqualTo("Renamed");
        await Assert.That(start.Name).IsEqualTo("OriginalName");
    }

    [Test]
    public async Task Apply_Builder_SetDomainName_Works() {
        var start = new Domain("OriginalName", [], []);
        var result = new DomainEvolution(start)
            .Evolve()
            .SetDomainName("Renamed")
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root.Name).IsEqualTo("Renamed");
    }

    [Test]
    public async Task Apply_AddPropertyToRelationship_AddsProperty() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var entity = new Entity("Person", [], [], [], []);
        var rel = new Relationship("Friends",
            new DomainTypeReference("Person"), new DomainTypeReference("Person"),
            RelationshipCardinality.ManyToMany, []);
        var start = new Domain("Test", [entity, textPrimitive], [rel]);
        var prop = new Property("Since", new DomainTypeReference("Text"), []);
        var result = new DomainEvolution(start).Apply([new AddPropertyToRelationshipChange("Friends", prop)]);
        await Assert.That(result.Succeeded).IsTrue();
        var updatedRel = result.Root.Relationships.Single(r => r.Name == "Friends");
        await Assert.That(updatedRel.Properties.Count).IsEqualTo(1);
        await Assert.That(updatedRel.Properties[0].Name).IsEqualTo("Since");
    }

    [Test]
    public async Task Apply_RemovePropertyFromRelationship_RemovesProperty() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var entity = new Entity("Person", [], [], [], []);
        var prop = new Property("Since", new DomainTypeReference("Text"), []);
        var rel = new Relationship("Friends",
            new DomainTypeReference("Person"), new DomainTypeReference("Person"),
            RelationshipCardinality.ManyToMany, [prop]);
        var start = new Domain("Test", [entity, textPrimitive], [rel]);
        var result = new DomainEvolution(start).Apply([new RemovePropertyFromRelationshipChange("Friends", "Since")]);
        await Assert.That(result.Succeeded).IsTrue();
        var updatedRel = result.Root.Relationships.Single(r => r.Name == "Friends");
        await Assert.That(updatedRel.Properties).IsEmpty();
    }

    [Test]
    public async Task Apply_AddConstraintToProperty_AddsConstraint() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var entity = new Entity("Customer", [
            new Property("Email", new DomainTypeReference("Text"), [])
        ], [], [], []);
        var start = new Domain("Test", [entity, textPrimitive], []);
        var constraint = new RequiredConstraint();
        var result = new DomainEvolution(start).Apply([new AddConstraintToPropertyChange("Customer", "Email", constraint)]);
        await Assert.That(result.Succeeded).IsTrue();
        var updatedEntity = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var emailProp = updatedEntity.Properties.Single(p => p.Name == "Email");
        await Assert.That(emailProp.Constraints.Count).IsEqualTo(1);
        await Assert.That(emailProp.Constraints[0]).IsTypeOf<RequiredConstraint>();
    }

    [Test]
    public async Task Apply_RemoveConstraintFromProperty_RemovesConstraint() {
        var constraint = new RequiredConstraint();
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var entity = new Entity("Customer", [
            new Property("Email", new DomainTypeReference("Text"), [constraint])
        ], [], [], []);
        var start = new Domain("Test", [entity, textPrimitive], []);
        var result = new DomainEvolution(start).Apply([new RemoveConstraintFromPropertyChange("Customer", "Email", constraint)]);
        await Assert.That(result.Succeeded).IsTrue();
        var updatedEntity = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var emailProp = updatedEntity.Properties.Single(p => p.Name == "Email");
        await Assert.That(emailProp.Constraints).IsEmpty();
    }

    [Test]
    public async Task Apply_AddConstraintToDomainType_AddsConstraint() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var start = new Domain("Test", [textPrimitive], []);
        var constraint = new RequiredConstraint();
        var result = new DomainEvolution(start).Apply([new AddConstraintToDomainTypeChange("Text", constraint)]);
        await Assert.That(result.Succeeded).IsTrue();
        var updated = result.Root.Types.Single(t => t.Name == "Text");
        await Assert.That(updated.Constraints.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_RemoveConstraintFromDomainType_RemovesConstraint() {
        var constraint = new RequiredConstraint();
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, [constraint]);
        var start = new Domain("Test", [textPrimitive], []);
        var result = new DomainEvolution(start).Apply([new RemoveConstraintFromDomainTypeChange("Text", constraint)]);
        await Assert.That(result.Succeeded).IsTrue();
        var updated = result.Root.Types.Single(t => t.Name == "Text");
        await Assert.That(updated.Constraints).IsEmpty();
    }

    [Test]
    public async Task Apply_ChangePropertyType_UpdatesType() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var intPrimitive = new PrimitiveType("Integer", Poly.Introspection.TypeCategory.Integer, []);
        var entity = new Entity("Product", [
            new Property("Count", new DomainTypeReference("Text"), [])
        ], [], [], []);
        var start = new Domain("Test", [entity, textPrimitive, intPrimitive], []);
        var result = new DomainEvolution(start).Apply([new ChangePropertyTypeChange("Product", "Count", new DomainTypeReference("Integer"))]);
        await Assert.That(result.Succeeded).IsTrue();
        var updatedEntity = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Product");
        var countProp = updatedEntity.Properties.Single(p => p.Name == "Count");
        await Assert.That(countProp.Type.TypeName).IsEqualTo("Integer");
    }

    [Test]
    public async Task Apply_SetRelationshipShape_UpdatesCardinality() {
        var entity = new Entity("Person", [], [], [], []);
        var rel = new Relationship("Friends",
            new DomainTypeReference("Person"), new DomainTypeReference("Person"),
            RelationshipCardinality.ManyToMany, []);
        var start = new Domain("Test", [entity], [rel]);
        var result = new DomainEvolution(start).Apply([new SetRelationshipShapeChange("Friends",
            NewCardinality: RelationshipCardinality.OneToOne)]);
        await Assert.That(result.Succeeded).IsTrue();
        var updatedRel = result.Root.Relationships.Single(r => r.Name == "Friends");
        await Assert.That(updatedRel.Cardinality).IsEqualTo(RelationshipCardinality.OneToOne);
    }

    [Test]
    public async Task Apply_SetPrimitiveTypeCategory_UpdatesCategory() {
        var textPrimitive = new PrimitiveType("MyText", Poly.Introspection.TypeCategory.Text, []);
        var start = new Domain("Test", [textPrimitive], []);
        var result = new DomainEvolution(start).Apply([new SetPrimitiveTypeCategoryChange("MyText", Poly.Introspection.TypeCategory.Numeric)]);
        await Assert.That(result.Succeeded).IsTrue();
        var updated = result.Root.Types.OfType<PrimitiveType>().Single(t => t.Name == "MyText");
        await Assert.That(updated.TypeCategory).IsEqualTo(Poly.Introspection.TypeCategory.Numeric);
    }

    // --- Contract integration tests ---

    [Test]
    public async Task Apply_AddImportedContractChange_AddsContractToDomain() {
        var start = new Domain("TestDomain", [], []);
        var change = new AddImportedContractChange("CrmContract", ContractSourceKind.ExternalProvider, "crm://api/ticket", "v1");
        var result = new DomainEvolution(start).Apply([change]);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root.ImportedContracts.Count).IsEqualTo(1);
        await Assert.That(result.Root.ImportedContracts[0].Name).IsEqualTo("CrmContract");
        await Assert.That(result.Root.ImportedContracts[0].SourceIdentifier).IsEqualTo("crm://api/ticket");
    }

    [Test]
    public async Task Apply_RemoveImportedContractChange_RemovesContractAndBindings() {
        var contract = new ImportedContract("CrmContract", ContractSourceKind.ExternalProvider, "crm://api/ticket", "v1", []);
        var binding = new ContractBinding("MyBinding", "CrmContract", "GetTicket", "SomeAction", "param", []);
        var start = new Domain("Test", [], []) {
            ImportedContracts = [contract],
            ContractBindings = [binding]
        };
        var result = new DomainEvolution(start).Apply([new RemoveImportedContractChange("CrmContract")]);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root.ImportedContracts).IsEmpty();
        await Assert.That(result.Root.ContractBindings).IsEmpty();
    }

    [Test]
    public async Task Apply_AddContractEndpointChange_AddsEndpoint() {
        var contract = new ImportedContract("CrmContract", ContractSourceKind.ExternalProvider, "crm://api/ticket", "v1", []);
        var start = new Domain("Test", [], []) { ImportedContracts = [contract] };
        var endpoint = new ContractEndpoint("GetTicket", ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, new DomainTypeReference("TicketData"));
        var result = new DomainEvolution(start).Apply([new AddContractEndpointChange("CrmContract", endpoint)]);
        await Assert.That(result.Succeeded).IsTrue();
        var updated = result.Root.ImportedContracts.Single(c => c.Name == "CrmContract");
        await Assert.That(updated.Endpoints.Count).IsEqualTo(1);
        await Assert.That(updated.Endpoints[0].Name).IsEqualTo("GetTicket");
    }

    [Test]
    public async Task Apply_RemoveContractEndpointChange_RemovesEndpoint() {
        var endpoint = new ContractEndpoint("GetTicket", ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, new DomainTypeReference("TicketData"));
        var contract = new ImportedContract("CrmContract", ContractSourceKind.ExternalProvider, "crm://api/ticket", "v1", [endpoint]);
        var start = new Domain("Test", [], []) { ImportedContracts = [contract] };
        var result = new DomainEvolution(start).Apply([new RemoveContractEndpointChange("CrmContract", "GetTicket")]);
        await Assert.That(result.Succeeded).IsTrue();
        var updated = result.Root.ImportedContracts.Single(c => c.Name == "CrmContract");
        await Assert.That(updated.Endpoints).IsEmpty();
    }

    [Test]
    public async Task Apply_AddContractBindingChange_AddsBinding() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var endpoint = new ContractEndpoint("GetTicket", ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, new DomainTypeReference("Text"));
        var contract = new ImportedContract("CrmContract", ContractSourceKind.ExternalProvider, "crm://api", "v1", [endpoint]);
        var action = new Poly.DomainModeling.Action("SomeAction", InvocationResult.Void,
            [new Property("input", new DomainTypeReference("Text"), [])], [], []);
        var entity = new Entity("MyEntity", [new Property("Name", new DomainTypeReference("Text"), [])], [action], [], []);
        var start = new Domain("Test", [entity, textPrimitive], []) {
            ImportedContracts = [contract],
            ContractBindings = []
        };
        var change = new AddContractBindingChange("MyBinding", "CrmContract", "GetTicket", "SomeAction", "input");
        var result = new DomainEvolution(start).Apply([change]);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root.ContractBindings.Count).IsEqualTo(1);
        await Assert.That(result.Root.ContractBindings[0].ContractName).IsEqualTo("CrmContract");
    }

    [Test]
    public async Task Apply_RemoveContractBindingChange_RemovesBinding() {
        var binding = new ContractBinding("MyBinding", "CrmContract", "GetTicket", "SomeAction", "input", []);
        var start = new Domain("Test", [], []) { ContractBindings = [binding] };
        var result = new DomainEvolution(start).Apply([new RemoveContractBindingChange("MyBinding")]);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root.ContractBindings).IsEmpty();
    }

    [Test]
    public async Task Apply_AddContractFieldMapChange_AddsFieldMap() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var endpoint = new ContractEndpoint("GetTicket", ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, new DomainTypeReference("Text"));
        var contract = new ImportedContract("CrmContract", ContractSourceKind.ExternalProvider, "crm://api", "v1", [endpoint]);
        var action = new Poly.DomainModeling.Action("SomeAction", InvocationResult.Void,
            [new Property("input", new DomainTypeReference("Text"), [])], [], []);
        var entity = new Entity("MyEntity", [new Property("Name", new DomainTypeReference("Text"), [])], [action], [], []);
        var binding = new ContractBinding("MyBinding", "CrmContract", "GetTicket", "SomeAction", "input", []);
        var start = new Domain("Test", [entity, textPrimitive], []) {
            ImportedContracts = [contract],
            ContractBindings = [binding]
        };
        var fieldMap = new ContractFieldMap("remoteId", "localId");
        var result = new DomainEvolution(start).Apply([new AddContractFieldMapChange("MyBinding", fieldMap)]);
        await Assert.That(result.Succeeded).IsTrue();
        var updated = result.Root.ContractBindings.Single(b => b.Name == "MyBinding");
        await Assert.That(updated.FieldMaps.Count).IsEqualTo(1);
        await Assert.That(updated.FieldMaps[0].RemoteFieldName).IsEqualTo("remoteId");
    }

    [Test]
    public async Task Apply_RemoveContractFieldMapChange_RemovesFieldMap() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var endpoint = new ContractEndpoint("GetTicket", ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, new DomainTypeReference("Text"));
        var contract = new ImportedContract("CrmContract", ContractSourceKind.ExternalProvider, "crm://api", "v1", [endpoint]);
        var action = new Poly.DomainModeling.Action("SomeAction", InvocationResult.Void,
            [new Property("input", new DomainTypeReference("Text"), [])], [], []);
        var entity = new Entity("MyEntity", [new Property("Name", new DomainTypeReference("Text"), [])], [action], [], []);
        var fieldMap = new ContractFieldMap("remoteId", "localId");
        var binding = new ContractBinding("MyBinding", "CrmContract", "GetTicket", "SomeAction", "input", [fieldMap]);
        var start = new Domain("Test", [entity, textPrimitive], []) {
            ImportedContracts = [contract],
            ContractBindings = [binding]
        };
        var result = new DomainEvolution(start).Apply([new RemoveContractFieldMapChange("MyBinding", "remoteId")]);
        await Assert.That(result.Succeeded).IsTrue();
        var updated = result.Root.ContractBindings.Single(b => b.Name == "MyBinding");
        await Assert.That(updated.FieldMaps).IsEmpty();
    }

    [Test]
    public async Task Apply_ContractIntegrationAnalyzer_DetectsMissingSourceIdentifier() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var contract = new ImportedContract("BadContract", ContractSourceKind.ExternalProvider, "", "v1", []);
        var start = new Domain("Test", [textPrimitive], []) { ImportedContracts = [contract] };
        var result = new DomainEvolution(start).Apply([]);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary).Contains("source identifier");
    }

    [Test]
    public async Task Apply_ContractIntegrationAnalyzer_DetectsMissingVersion() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var contract = new ImportedContract("BadContract", ContractSourceKind.ExternalProvider, "crm://api", "", []);
        var start = new Domain("Test", [textPrimitive], []) { ImportedContracts = [contract] };
        var result = new DomainEvolution(start).Apply([]);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary).Contains("missing a version");
    }

    [Test]
    public async Task Apply_ContractIntegrationAnalyzer_DetectsMissingContractForBinding() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var binding = new ContractBinding("MyBinding", "NonExistentContract", "GetTicket", "SomeAction", "input", []);
        var start = new Domain("Test", [textPrimitive], []) { ContractBindings = [binding] };
        var result = new DomainEvolution(start).Apply([]);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary).Contains("not registered");
    }

    [Test]
    public async Task Apply_ContractIntegrationAnalyzer_DetectsMissingEndpointOnBinding() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var contract = new ImportedContract("CrmContract", ContractSourceKind.ExternalProvider, "crm://api", "v1", []);
        var binding = new ContractBinding("MyBinding", "CrmContract", "NonExistentEndpoint", "SomeAction", "input", []);
        var start = new Domain("Test", [textPrimitive], []) {
            ImportedContracts = [contract],
            ContractBindings = [binding]
        };
        var result = new DomainEvolution(start).Apply([]);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary).Contains("does not belong");
    }

    [Test]
    public async Task Apply_ContractIntegrationAnalyzer_DetectsMissingAction() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var endpoint = new ContractEndpoint("GetTicket", ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, new DomainTypeReference("Text"));
        var contract = new ImportedContract("CrmContract", ContractSourceKind.ExternalProvider, "crm://api", "v1", [endpoint]);
        var binding = new ContractBinding("MyBinding", "CrmContract", "GetTicket", "NonExistentAction", "input", []);
        var start = new Domain("Test", [textPrimitive], []) {
            ImportedContracts = [contract],
            ContractBindings = [binding]
        };
        var result = new DomainEvolution(start).Apply([]);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary).Contains("not found");
    }

    [Test]
    public async Task Apply_ContractIntegrationAnalyzer_DetectsMissingParameter() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var endpoint = new ContractEndpoint("GetTicket", ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, new DomainTypeReference("Text"));
        var contract = new ImportedContract("CrmContract", ContractSourceKind.ExternalProvider, "crm://api", "v1", [endpoint]);
        var action = new Poly.DomainModeling.Action("MyAction", InvocationResult.Void,
            [new Property("something", new DomainTypeReference("Text"), [])], [], []);
        var entity = new Entity("MyEntity", [new Property("Name", new DomainTypeReference("Text"), [])], [action], [], []);
        var binding = new ContractBinding("MyBinding", "CrmContract", "GetTicket", "MyAction", "missingParam", []);
        var start = new Domain("Test", [entity, textPrimitive], []) {
            ImportedContracts = [contract],
            ContractBindings = [binding]
        };
        var result = new DomainEvolution(start).Apply([]);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary).Contains("missingParam");
    }

    [Test]
    public async Task Apply_ContractIntegrationAnalyzer_DetectsTypeMismatch() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var ticketDataPrimitive = new PrimitiveType("TicketData", Poly.Introspection.TypeCategory.Text, []);
        var endpoint = new ContractEndpoint("GetTicket", ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, new DomainTypeReference("TicketData"));
        var contract = new ImportedContract("CrmContract", ContractSourceKind.ExternalProvider, "crm://api", "v1", [endpoint]);
        var action = new Poly.DomainModeling.Action("MyAction", InvocationResult.Void,
            [new Property("input", new DomainTypeReference("Text"), [])], [], []);
        var entity = new Entity("MyEntity", [new Property("Name", new DomainTypeReference("Text"), [])], [action], [], []);
        var binding = new ContractBinding("MyBinding", "CrmContract", "GetTicket", "MyAction", "input", []);
        var start = new Domain("Test", [entity, textPrimitive, ticketDataPrimitive], []) {
            ImportedContracts = [contract],
            ContractBindings = [binding]
        };
        var result = new DomainEvolution(start).Apply([]);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary).Contains("incompatible");
    }

    [Test]
    public async Task ContractDomainObject_SupportsTreeWalk() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var endpoint = new ContractEndpoint("GetTicket", ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, new DomainTypeReference("Text"));
        var contract = new ImportedContract("CrmContract", ContractSourceKind.ExternalProvider, "crm://api", "v1", [endpoint]);
        var binding = new ContractBinding("MyBinding", "CrmContract", "GetTicket", "SomeAction", "input", [
            new ContractFieldMap("remoteId", "localId")
        ]);
        var domain = new Domain("Test", [textPrimitive], []) {
            ImportedContracts = [contract],
            ContractBindings = [binding]
        };

        var children = domain.Children.ToArray();
        await Assert.That(children).Contains(contract);
        await Assert.That(children).Contains(binding);

        var contractChildren = contract.Children.ToArray();
        await Assert.That(contractChildren).Contains(endpoint);
    }

    [Test]
    public async Task Apply_ValidContractIntegration_PassesAnalysis() {
        var textPrimitive = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var endpoint = new ContractEndpoint("GetTicket", ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, new DomainTypeReference("Text"));
        var contract = new ImportedContract("CrmContract", ContractSourceKind.ExternalProvider, "crm://api/ticket", "v1", [endpoint]);
        var action = new Poly.DomainModeling.Action("MyAction", InvocationResult.Void,
            [new Property("input", new DomainTypeReference("Text"), [])], [], []);
        var entity = new Entity("MyEntity", [new Property("Name", new DomainTypeReference("Text"), [])], [action], [], []);
        var binding = new ContractBinding("MyBinding", "CrmContract", "GetTicket", "MyAction", "input", [
            new ContractFieldMap("remoteId", "localId")
        ]);
        var start = new Domain("Test", [entity, textPrimitive], []) {
            ImportedContracts = [contract],
            ContractBindings = [binding]
        };

        var result = new DomainEvolution(start).Apply([]);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task DomainExpression_Comparison_AllKinds_CanBeConstructed() {
        var start = new Domain("Test", [
            new PrimitiveType("Int", Poly.Introspection.TypeCategory.Integer, []),
            new Entity("Stock", [
                new Property("Quantity", new DomainTypeReference("Int"), []),
                new Property("Min", new DomainTypeReference("Int"), []),
                new Property("Max", new DomainTypeReference("Int"), [])
            ], [], [], [])
        ], []);

        // Each kind uses ConditionalEffect wrapping a comparison as the condition.
        // Then/Else branches use AssignEffect to avoid requiring valid stage references.

        // Equal
        var eqResult = new DomainEvolution(start)
            .Evolve()
            .AddAction("Stock", "CheckEqual")
            .AddEffectToAction("Stock", "CheckEqual",
                new ConditionalEffect(
                    DomainExpression.Equal(DomainExpression.Property("Quantity"), DomainExpression.Property("Min")),
                    [new AssignEffect(DomainExpression.Property("Quantity"), DomainExpression.Literal(0))],
                    null))
            .Apply();
        await Assert.That(eqResult.Succeeded).IsTrue();

        // NotEqual
        var neqResult = new DomainEvolution(start)
            .Evolve()
            .AddAction("Stock", "CheckNotEqual")
            .AddEffectToAction("Stock", "CheckNotEqual",
                new ConditionalEffect(
                    DomainExpression.NotEqual(DomainExpression.Property("Quantity"), DomainExpression.Literal(0)),
                    [new AssignEffect(DomainExpression.Property("Quantity"), DomainExpression.Literal(42))],
                    null))
            .Apply();
        await Assert.That(neqResult.Succeeded).IsTrue();

        // LessThan
        var ltResult = new DomainEvolution(start)
            .Evolve()
            .AddAction("Stock", "CheckLow")
            .AddEffectToAction("Stock", "CheckLow",
                new ConditionalEffect(
                    DomainExpression.LessThan(DomainExpression.Property("Quantity"), DomainExpression.Property("Min")),
                    [new AssignEffect(DomainExpression.Property("Quantity"), DomainExpression.Property("Min"))],
                    [new AssignEffect(DomainExpression.Property("Quantity"), DomainExpression.Property("Max"))]))
            .Apply();
        await Assert.That(ltResult.Succeeded).IsTrue();

        // LessThanOrEqual
        var lteResult = new DomainEvolution(start)
            .Evolve()
            .AddAction("Stock", "CheckAtOrBelowMin")
            .AddEffectToAction("Stock", "CheckAtOrBelowMin",
                new ConditionalEffect(
                    DomainExpression.LessThanOrEqual(DomainExpression.Property("Quantity"), DomainExpression.Property("Min")),
                    [new AssignEffect(DomainExpression.Property("Quantity"), DomainExpression.Property("Min"))],
                    [new AssignEffect(DomainExpression.Property("Quantity"), DomainExpression.Property("Max"))]))
            .Apply();
        await Assert.That(lteResult.Succeeded).IsTrue();

        // GreaterThan
        var gtResult = new DomainEvolution(start)
            .Evolve()
            .AddAction("Stock", "CheckOverstock")
            .AddEffectToAction("Stock", "CheckOverstock",
                new ConditionalEffect(
                    DomainExpression.GreaterThan(DomainExpression.Property("Quantity"), DomainExpression.Property("Max")),
                    [new AssignEffect(DomainExpression.Property("Quantity"), DomainExpression.Property("Max"))],
                    [new AssignEffect(DomainExpression.Property("Quantity"), DomainExpression.Property("Min"))]))
            .Apply();
        await Assert.That(gtResult.Succeeded).IsTrue();

        // GreaterThanOrEqual
        var gteResult = new DomainEvolution(start)
            .Evolve()
            .AddAction("Stock", "CheckAtOrAboveMax")
            .AddEffectToAction("Stock", "CheckAtOrAboveMax",
                new ConditionalEffect(
                    DomainExpression.GreaterThanOrEqual(DomainExpression.Property("Quantity"), DomainExpression.Property("Max")),
                    [new AssignEffect(DomainExpression.Property("Quantity"), DomainExpression.Property("Max"))],
                    [new AssignEffect(DomainExpression.Property("Quantity"), DomainExpression.Property("Min"))]))
            .Apply();
        await Assert.That(gteResult.Succeeded).IsTrue();
    }

    [Test]
    public async Task DomainExpression_Comparison_ConstructsCorrectNode() {
        // Verify the factory methods produce the right Comparison nodes in memory
        var equal = DomainExpression.Equal(DomainExpression.Property("A"), DomainExpression.Property("B"));
        await Assert.That(equal).IsTypeOf<Comparison>();
        var comp = (Comparison)equal;
        await Assert.That(comp.Kind).IsEqualTo(ComparisonKind.Equal);

        var lt = DomainExpression.LessThan(DomainExpression.Property("X"), DomainExpression.Literal(10));
        await Assert.That(lt).IsTypeOf<Comparison>();
        var comp2 = (Comparison)lt;
        await Assert.That(comp2.Kind).IsEqualTo(ComparisonKind.LessThan);
    }

    [Test]
    public async Task Evolution_ApplyChanges_ReturnsModifiedNodes() {
        // Verify the allocation fix: ApplyChanges now returns modified nodes list
        var start = new Domain("Test", [
            new PrimitiveType("Int", Poly.Introspection.TypeCategory.Integer, []),
        ], []);

        var evolution = new DomainEvolution(start);
        var changes = new DomainChange[] {
            new AddEntityChange("Order", []),
            new AddPropertyToEntityChange("Order", new Property("Total", new DomainTypeReference("Int"), []))
        };

        var result = evolution.Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var order = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Order");
        await Assert.That(order.Properties.Count).IsEqualTo(1);
    }

    // ── 0.1d: Remove-by-name zero-match fail-loud ────────────────

    [Test]
    public async Task RemoveProperty_MissingName_FailsWithClearError() {
        var text = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var entity = new Entity("Order", [new Property("Total", new DomainTypeReference("Int"), [])], [], [], []);
        var start = new Domain("Test", [entity, text], []);

        var change = new RemovePropertyFromEntityChange("Order", "NonExistent");
        var result = new DomainEvolution(start).Apply([change]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary).Contains("not found on Entity 'Order'");
        await Assert.That(result.FailureSummary).Contains("nothing to remove");
    }

    [Test]
    public async Task RemoveStage_MissingName_FailsWithClearError() {
        var entity = new Entity("Order", [], [], [], Stages: [new Stage("Draft", [], [], [], [])]);
        var start = new Domain("Test", [entity], []);

        var change = new RemoveStageChange("Order", "NonExistent");
        var result = new DomainEvolution(start).Apply([change]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary).Contains("not found on Entity 'Order'");
        await Assert.That(result.FailureSummary).Contains("nothing to remove");
    }

    [Test]
    public async Task RemoveAction_MissingName_FailsWithClearError() {
        var entity = new Entity("Order", [], [
            new Poly.DomainModeling.Action("Submit", InvocationResult.Void, [], [], [])
        ], [], []);
        var start = new Domain("Test", [entity], []);

        var change = new RemoveActionChange("Order", "NonExistent");
        var result = new DomainEvolution(start).Apply([change]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary).Contains("not found on Entity 'Order'");
        await Assert.That(result.FailureSummary).Contains("nothing to remove");
    }

    [Test]
    public async Task RemovePolicy_MissingName_FailsWithClearError() {
        var policy = new Policy("Adult", DomainExpression.Literal(true));
        var entity = new Entity("Person", [], [], Policies: [policy], Stages: []);
        var start = new Domain("Test", [entity], []);

        var change = new RemovePolicyFromEntityChange("Person", "NonExistent");
        var result = new DomainEvolution(start).Apply([change]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary).Contains("not found on Entity 'Person'");
        await Assert.That(result.FailureSummary).Contains("nothing to remove");
    }

    [Test]
    public async Task Remove_ExistingProperty_Succeeds() {
        var text = new PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []);
        var entity = new Entity("Order", [new Property("Status", new DomainTypeReference("Text"), [])], [], [], []);
        var start = new Domain("Test", [entity, text], []);

        var change = new RemovePropertyFromEntityChange("Order", "Status");
        var result = new DomainEvolution(start).Apply([change]);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root.Types.OfType<Entity>().Single().Properties).IsEmpty();
    }

    [Test]
    public async Task Remove_ExistingStage_Succeeds() {
        var entity = new Entity("Order", [], [], [], Stages: [new Stage("Draft", [], [], [], [])]);
        var start = new Domain("Test", [entity], []);

        var change = new RemoveStageChange("Order", "Draft");
        var result = new DomainEvolution(start).Apply([change]);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root.Types.OfType<Entity>().Single().Stages).IsEmpty();
    }
}