using Poly.DomainModeling;
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

        var change = new AddEntityChange("Order", [
            new Property("Id", new DomainTypeReference("Text"), [])
        ]);

        var evolution = new DomainEvolution(start);
        var result = evolution.Apply([change]);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.WasRolledBack).IsFalse(); // proposal accepted
        await Assert.That(result.Root).IsNotSameReferenceAs(start);

        var entities = result.Root.Types.OfType<Entity>().ToList();
        await Assert.That(entities.Count).IsEqualTo(1);
        await Assert.That(entities[0].Name).IsEqualTo("Order");
        await Assert.That(entities[0].Properties.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_RemoveEntityChange_RemovesFromNewRoot() {
        var entity = new Entity("Customer", [], [], [], [], []);
        var start = new Domain("TestDomain", [entity], []);

        var change = new RemoveEntityChange("Customer");

        var result = new DomainEvolution(start).Apply([change]);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root.Types.OfType<Entity>()).IsEmpty();
        await Assert.That(result.Root).IsNotSameReferenceAs(start);
    }

    [Test]
    public async Task Apply_AddPropertyToExistingEntity_Works() {
        var entity = new Entity("Order", [], [], [], [], []);
        var start = new Domain("Test", [entity], []);

        var newProp = new Property("Status", new DomainTypeReference("Text"), []);
        var change = new AddPropertyToEntityChange("Order", newProp);

        var result = new DomainEvolution(start).Apply([change]);

        var updated = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Order");
        await Assert.That(updated.Properties.Count).IsEqualTo(1);
        await Assert.That(updated.Properties[0].Name).IsEqualTo("Status");
    }

    [Test]
    public async Task Apply_MixedBatch_ProducesCorrectResult() {
        var start = new Domain("Test", [], []);

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
        // Create a domain with two entities
        var untouched = new Entity("Customer", [], [], [], [], []);
        var touched = new Entity("Order", [], [], [], [], []);
        var start = new Domain("Test", [untouched, touched], []);

        var originalUntouchedId = untouched.Id;

        // Change only affects "Order"
        var change = new AddPropertyToEntityChange("Order",
            new Property("Total", new DomainTypeReference("Int"), []));

        var result = new DomainEvolution(start).Apply([change]);

        var customerInResult = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Customer");

        // The untouched entity must have the exact same stable Id
        await Assert.That(customerInResult.Id).IsEqualTo(originalUntouchedId);
        await Assert.That(customerInResult).IsNotSameReferenceAs(untouched); // new Domain instance, but same logical node Id
    }

    [Test]
    public async Task Apply_AddStageChange_AddsStageToEntity() {
        var entity = new Entity("Person", [], [], [], [], []);
        var start = new Domain("Test", [entity], []);

        var change = new AddStageChange("Person", "Alive");

        var result = new DomainEvolution(start).Apply([change]);

        var updated = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");
        await Assert.That(updated.Stages.Count).IsEqualTo(1);
        await Assert.That(updated.Stages[0].Name).IsEqualTo("Alive");
        await Assert.That(updated.Stages[0].Parent).IsNull();
    }

    [Test]
    public async Task Apply_RemoveStageChange_RemovesStage() {
        var stage = new Stage("Alive", null, [], [], [], []);
        var entity = new Entity("Person", [], [], [], [], [stage]);
        var start = new Domain("Test", [entity], []);

        var change = new RemoveStageChange("Person", "Alive");

        var result = new DomainEvolution(start).Apply([change]);

        var updated = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");
        await Assert.That(updated.Stages).IsEmpty();
    }

    [Test]
    public async Task Apply_AddActionChange_AddsActionToEntity() {
        var entity = new Entity("Person", [], [], [], [], []);
        var start = new Domain("Test", [entity], []);

        var change = new AddActionChange("Person", "Die");

        var result = new DomainEvolution(start).Apply([change]);

        var updated = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");
        await Assert.That(updated.Actions.Count).IsEqualTo(1);
        await Assert.That(updated.Actions[0].Name).IsEqualTo("Die");
    }

    [Test]
    public async Task Apply_RemoveActionChange_RemovesAction() {
        var action = new Poly.DomainModeling.Action("Die", new InvocationResult([]), [], [], []);
        var entity = new Entity("Person", [], [], [action], [], []);
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

        // Second evolution: much more fluent, realistic incremental evolution of a lifecycle
        var result2 = new DomainEvolution(afterFirst.Root)
            .Evolve()
            // Add owned documents (ValueTypes) and events — key for PersonLifecycle style
            .AddValueType("BirthCertificate", new Property("Time", new DomainTypeReference("Timestamp"), []))
            .AddValueType("DeathCertificate",
                new Property("Time", new DomainTypeReference("Timestamp"), []),
                new Property("Cause", new DomainTypeReference("Text"), []))
            .AddEvent("Born", new Property("TimeOfBirth", new DomainTypeReference("Timestamp"), []))
            .AddEventReferenceToEntity("Person", "Born")

            .AddPropertyToEntity("Person", new Property("GivenName", new DomainTypeReference("Text"), []))
            .AddStageGuard("Person", "Alive", "HasBirthCert",
                DomainExpression.Exists(
                    DomainExpression.Owned("BirthCertificate", DomainExpression.Property("Time"))))
            .AddActionThatPublishesEvent("Person", "Alive", "Born")
            .AddAction("Person", "Register",
                result: new InvocationResult([new InvocationResult.Member("BirthCertificateId", new DomainTypeReference("Text"), [])]),
                parameters: [new Property("TimeOfBirth", new DomainTypeReference("Timestamp"), [])],
                effects: [new CreateEntityInstance(new DomainTypeReference("BirthCertificate"), [
                    new PropertyBinding("Time", DomainExpression.Parameter("TimeOfBirth"))
                ])])
            .AddPolicyToAction("Person", "Register", "ValidRegistration",
                DomainExpression.Exists(DomainExpression.Parameter("TimeOfBirth")))
            .AddAction("Person", "Die",
                result: new InvocationResult([new InvocationResult.Member("Success", new DomainTypeReference("Boolean"), [])]),
                effects: [
                    new CreateEntityInstance(new DomainTypeReference("DeathCertificate"), [
                        new PropertyBinding("Time", DomainExpression.Parameter("TimeOfDeath")),
                        new PropertyBinding("Cause", DomainExpression.Parameter("CauseOfDeath"))
                    ]),
                    new StageTransitionEffect(new StageReference("Dead"))
                ])
            .AddPublishEventEffect("Person", "Alive", "Died")
            .AddRelationship("Friends", "Person", "Person", RelationshipCardinality.ManyToMany)
            .Apply();

        await Assert.That(result2.Succeeded).IsTrue();

        var finalPerson = result2.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");
        await Assert.That(finalPerson.Properties.Count).IsEqualTo(1);
        await Assert.That(finalPerson.Stages.Count).IsEqualTo(2);

        var alive = finalPerson.Stages.Single(s => s.Name == "Alive");
        await Assert.That(alive.Policies.Count).IsEqualTo(1);
        await Assert.That(alive.OnEntryEffects.Count).IsEqualTo(1);
        await Assert.That(alive.OnExitEffects.Count).IsEqualTo(1);

        var register = finalPerson.Actions.Single(a => a.Name == "Register");
        await Assert.That(register.Effects.Count).IsEqualTo(1);
        await Assert.That(register.Policies.Count).IsEqualTo(1);
        await Assert.That(register.Parameters.Count).IsEqualTo(1);
        await Assert.That(register.Parameters[0].Name).IsEqualTo("TimeOfBirth");
        await Assert.That(register.Result.Members.Count).IsEqualTo(1);
        await Assert.That(register.Result.Members[0].Name).IsEqualTo("BirthCertificateId");

        var die = finalPerson.Actions.Single(a => a.Name == "Die");
        await Assert.That(die.Effects.Count).IsEqualTo(2); // Create + Transition
        await Assert.That(die.Result.Members.Count).IsEqualTo(1);

        await Assert.That(result2.Root.Relationships.Count).IsEqualTo(1);
        await Assert.That(result2.Root.Relationships[0].Name).IsEqualTo("Friends");

        // Verify ValueTypes and Events were added
        var valueTypes = result2.Root.Types.OfType<Poly.DomainModeling.ValueType>().ToList();
        await Assert.That(valueTypes.Count).IsEqualTo(2);
        await Assert.That(valueTypes.Any(v => v.Name == "BirthCertificate")).IsTrue();
        await Assert.That(valueTypes.Any(v => v.Name == "DeathCertificate")).IsTrue();

        await Assert.That(finalPerson.Events.Count).IsEqualTo(1);
        await Assert.That(finalPerson.Events[0].TypeName).IsEqualTo("Born");
    }

    [Test]
    public async Task Apply_AddEffectToActionChange_AttachesCreateEffect() {
        // Setup: Person entity with a Die action
        var dieAction = new Poly.DomainModeling.Action("Die", new InvocationResult([]), [], [], []);
        var person = new Entity("Person", [], [], [dieAction], [], []);
        var start = new Domain("Test", [person], []);

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
        var dieAction = new Poly.DomainModeling.Action("Die", new InvocationResult([]), [], [], []);
        var person = new Entity("Person", [], [], [dieAction], [], []);
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

        // Step 1: Core structure
        var step1 = evolution.Apply(new DomainChange[]
        {
            new AddEntityChange("Person", []),
            new AddStageChange("Person", "Alive"),
            new AddStageChange("Person", "Dead"),
            new AddActionChange("Person", "Die")
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
        var entity = new Entity("Person", [], [], [], [], []);
        var start = new Domain("Test", [entity], []);

        var policy = new Policy("HasBirthCert", DomainExpression.Exists(
            DomainExpression.Owned("BirthCertificate", DomainExpression.Property("Time"))));

        var result = new DomainEvolution(start).Apply([new AddPolicyToEntityChange("Person", policy)]);

        var updated = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Person");
        await Assert.That(updated.Policies.Count).IsEqualTo(1);
        await Assert.That(updated.Policies[0].Name).IsEqualTo("HasBirthCert");
    }

    [Test]
    public async Task Fluent_AddPolicyToStage_Works() {
        var start = new Domain("Test", [], []);

        var result = new DomainEvolution(start)
            .Evolve()
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
}