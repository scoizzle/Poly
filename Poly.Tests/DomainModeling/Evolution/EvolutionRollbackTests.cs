using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Evolution;

/// <summary>
/// Tests that prove the analysis gate + immutability rollback semantics.
/// These are the behavioral tests that gate M2 correctness.
/// </summary>
public class EvolutionRollbackTests {
    [Test]
    public async Task SuccessfulApply_ReturnsNewRoot_OriginalUnchanged() {
        var original = DomainFactory.Create("Test");
        var result = new DomainEvolution(original).Evolve()
            .AddEntity("Order")
            .Apply();

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.WasRolledBack).IsFalse();
        await Assert.That(result.Root).IsNotSameReferenceAs(original);

        // Original domain has no changes
        var entities = original.Types.OfType<Entity>().ToList();
        await Assert.That(entities).IsEmpty();
    }

    [Test]
    public async Task FailedApply_DuplicateEntity_WasRolledBack_AndDiagnosticsPopulated() {
        var original = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order"));

        // Try to add a duplicate entity name
        var result = new DomainEvolution(original).Evolve()
            .AddEntity("Order")
            .Apply();

        await Assert.That(result.WasRolledBack).IsTrue();
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.HasStructuralFailure).IsTrue();
        await Assert.That(result.FailureSummary).IsNotNull();
        await Assert.That(result.FailureSummary!.Contains("Order")).IsTrue();
    }

    [Test]
    public async Task FailedApply_UnknownStageParent_WasRolledBack_AndDiagnosticsPopulated() {
        var original = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order"));

        var result = new DomainEvolution(original).Evolve()
            .AddStage("Order", "Draft", parentName: "NonExistent")
            .Apply();

        await Assert.That(result.WasRolledBack).IsTrue();
        await Assert.That(result.FailureSummary).IsNotNull();
    }

    [Test]
    public async Task FailedApply_OriginalDomainIdentityPreserved() {
        var original = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order"));

        var originalId = original.Id;
        var originalTypeCount = original.Types.Count;

        var result = new DomainEvolution(original).Evolve()
            .AddEntity("Order") // duplicate
            .Apply();

        await Assert.That(result.WasRolledBack).IsTrue();
        // The returned root should be the same immutable instance
        await Assert.That(result.Root).IsSameReferenceAs(original);
        await Assert.That(result.Root.Id).IsEqualTo(originalId);
        await Assert.That(result.Root.Types.Count).IsEqualTo(originalTypeCount);
    }

    [Test]
    public async Task BatchApply_Atomicity_PartialFailure_RollsBackEntireBatch() {
        // Batch: valid entity + invalid duplicate + valid property
        var original = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order"));

        var result = new DomainEvolution(original).Apply([
            new AddEntityChange("Customer", []),               // valid
            new AddEntityChange("Order", []),                  // duplicate → error
            new AddPropertyToEntityChange("Customer",
                new Property("Name", new DomainTypeReference("Text"), [])), // would be valid
        ]);

        // The entire batch must be rolled back
        await Assert.That(result.WasRolledBack).IsTrue();
        await Assert.That(result.Root).IsSameReferenceAs(original);

        // Customer should NOT exist in original
        var customers = original.Types.OfType<Entity>().Where(e => e.Name == "Customer").ToList();
        await Assert.That(customers).IsEmpty();
    }

    [Test]
    public async Task SuccessTrace_ContainsStepDescriptions() {
        var original = DomainFactory.Create("Test");

        var result = new DomainEvolution(original).Evolve()
            .AddEntity("Order")
            .AddEntity("Customer")
            .Apply();

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Trace.Steps.Count).IsEqualTo(2);
        await Assert.That(result.Trace.Steps[0].ChangeDescription).Contains("Order");
        await Assert.That(result.Trace.Steps[1].ChangeDescription).Contains("Customer");
        await Assert.That(result.Trace.RolledBack).IsFalse();
        await Assert.That(result.Trace.ErrorCount).IsEqualTo(0);
    }

    [Test]
    public async Task FailureTrace_HasRolledBackFlagAndErrors() {
        var original = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order"));

        var result = new DomainEvolution(original).Evolve()
            .AddEntity("Order") // duplicate
            .AddStage("Order", "Draft")
            .Apply();

        await Assert.That(result.WasRolledBack).IsTrue();
        await Assert.That(result.Trace.RolledBack).IsTrue();
        await Assert.That(result.Trace.ErrorCount).IsGreaterThan(0);
        // Steps still describe all attempted changes
        await Assert.That(result.Trace.Steps.Count).IsEqualTo(2);
    }

    [Test]
    public async Task MultiStepEvolution_SequentialSuccesses() {
        var domain = DomainFactory.Create("Test");

        // Step 1
        var r1 = new DomainEvolution(domain).Evolve()
            .AddEntity("Order")
            .Apply();
        await Assert.That(r1.Succeeded).IsTrue();

        // Step 2 — evolve from step 1's root
        var r2 = new DomainEvolution(r1.Root).Evolve()
            .AddPropertyToEntity("Order", new Property("Status", new DomainTypeReference("Text"), []))
            .AddStage("Order", "Draft")
            .Apply();
        await Assert.That(r2.Succeeded).IsTrue();

        // Step 3 — add action
        var r3 = new DomainEvolution(r2.Root).Evolve()
            .AddAction("Order", "Submit")
            .Apply();
        await Assert.That(r3.Succeeded).IsTrue();

        // Final entity should have everything
        var order = r3.Root.Types.OfType<Entity>().Single();
        await Assert.That(order.Properties.Count).IsEqualTo(1);
        await Assert.That(order.Stages.Count).IsEqualTo(1);
        await Assert.That(order.Actions.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RollbackThenSuccess_DoesNotCorrupt() {
        var domain = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order"));

        // First attempt: fails
        var r1 = new DomainEvolution(domain).Evolve()
            .AddEntity("Order") // duplicate
            .Apply();
        await Assert.That(r1.WasRolledBack).IsTrue();

        // Second attempt: succeeds
        var r2 = new DomainEvolution(r1.Root).Evolve()
            .AddStage("Order", "Draft")
            .Apply();
        await Assert.That(r2.Succeeded).IsTrue();

        var order = r2.Root.Types.OfType<Entity>().Single();
        await Assert.That(order.Stages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_EmptyChangeList_ReturnsIdentityResult() {
        var domain = DomainFactory.Create("Test");
        var result = new DomainEvolution(domain).Apply([]);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root).IsSameReferenceAs(domain);
        await Assert.That(result.Trace.Steps).IsEmpty();
    }

    [Test]
    public async Task Apply_StageWithOnEntryEffect_Succeeds() {
        var domain = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order")
                   .AddEventToEntity("Order", "OrderCreated"));

        var result = new DomainEvolution(domain).Evolve()
            .AddStage("Order", "Draft", onEntryEffects:
            [
                new PublishEventEffect(new DomainTypeReference("OrderCreated"), []),
            ])
            .Apply();

        await Assert.That(result.Succeeded).IsTrue();

        var order = result.Root.Types.OfType<Entity>().Single();
        var draft = order.Stages.Single(s => s.Name == "Draft");
        await Assert.That(draft.OnEntryEffects.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_HasInformationDiagnostics_ForEachStep() {
        var domain = DomainFactory.Create("Test");
        var result = new DomainEvolution(domain).Evolve()
            .AddEntity("Order")
            .AddEntity("Customer")
            .Apply();

        var infoCount = result.Analysis.Diagnostics
            .Count(d => d.Severity == DiagnosticSeverity.Information);

        // Each successful evolution step should add an EVOLUTION_STEP info diagnostic
        await Assert.That(infoCount).IsGreaterThanOrEqualTo(2);
    }

    // ── Missing-target fail-loud (RequireUpdate + evalErrors → rollback) ───

    [Test]
    public async Task Apply_AddPropertyToMissingEntity_FailsLoudAndRollsBack() {
        var domain = new Domain("Test", [], []);
        var result = new DomainEvolution(domain).Evolve()
            .AddPropertyToEntity("NonExistent",
                new Property("X", new DomainTypeReference("Text"), []))
            .Apply();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.WasRolledBack).IsTrue();
        await Assert.That(result.Root).IsSameReferenceAs(domain);
    }

    // ── Missing child target fails loud ─────────────────────────────
    // When entity exists but stage/property doesn't, Apply must fail.

    [Test]
    public async Task AddActionToMissingStage_OnExistingEntity_FailsLoud() {
        var original = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order").AddStage("Order", "Active"));

        // Try adding action to a stage that doesn't exist
        var result = new DomainEvolution(original).Evolve()
            .AddActionToStage("Order", "NonExistentStage", "Submit")
            .Apply();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.WasRolledBack).IsTrue();
        await Assert.That(result.Root).IsSameReferenceAs(original);
    }

    [Test]
    public async Task AddPolicyToMissingStage_OnExistingEntity_FailsLoud() {
        var original = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order"));

        var result = new DomainEvolution(original).Evolve()
            .AddStage("Order", "Active")
            .AddPolicyToStage("Order", "NonExistentStage", "Guard",
                DomainExpression.GreaterThanOrEqual(
                    DomainExpression.Property("Total"),
                    DomainExpression.Literal(0)))
            .Apply();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.WasRolledBack).IsTrue();
    }

    [Test]
    public async Task RemovePolicyFromMissingStage_OnExistingEntity_FailsLoud() {
        var original = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order"));

        var withStage = new DomainEvolution(original).Evolve()
            .AddStage("Order", "Active")
            .AddPolicyToStage("Order", "Active", "G", DomainExpression.Literal(true))
            .Apply();
        await Assert.That(withStage.Succeeded).IsTrue();

        // Now target a non-existent stage for removal
        var result = new DomainEvolution(withStage.Root)
            .Apply([new RemovePolicyFromStageChange("Order", "NonExistentStage", "G")]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.WasRolledBack).IsTrue();
    }

    [Test]
    public async Task AddOnEntryEffectToMissingStage_OnExistingEntity_FailsLoud() {
        var original = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order").AddStage("Order", "Active"));

        var result = new DomainEvolution(original).Evolve()
            .AddOnEntryEffect("Order", "MissingStage", new StageTransitionEffect(new StageReference("Active")))
            .Apply();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.WasRolledBack).IsTrue();
    }

    [Test]
    public async Task ChangeTypeOfMissingProperty_OnExistingEntity_FailsLoud() {
        var original = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order")
                .AddPropertyToEntity("Order", new Property("Name", new DomainTypeReference("Text"), [])));

        // ChangePropertyTypeChange goes through UpdateProperty which now fails
        // when the property name doesn't exist on the entity.
        var result = new DomainEvolution(original)
            .Apply([new ChangePropertyTypeChange("Order", "NonExistentProp",
                new DomainTypeReference("Number"))]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.WasRolledBack).IsTrue();
    }

    [Test]
    public async Task AddConstraintToMissingProperty_OnExistingEntity_FailsLoud() {
        var original = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order"));

        var result = new DomainEvolution(original)
            .Apply([new AddConstraintToPropertyChange("Order", "NonExistent",
                new RequiredConstraint())]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.WasRolledBack).IsTrue();
    }

    [Test]
    public async Task SuccessfulStageUpdate_StillWorks() {
        var original = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order").AddStage("Order", "Active"));

        // Adding action to an existing stage should still succeed
        var result = new DomainEvolution(original).Evolve()
            .AddActionToStage("Order", "Active", "Submit")
            .Apply();

        await Assert.That(result.Succeeded).IsTrue();
        var entity = result.Root.Types.OfType<Entity>().Single();
        var stage = entity.Stages.Single();
        await Assert.That(stage.Actions.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AddActionToStage_CreatesNewStageLocalAction_NotEntityLevel() {
        // Proves that AddActionToStageChange creates a new stage-local Action,
        // not referencing an entity-level action with the same name.
        var original = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order")
                .AddStage("Order", "Active")
                .AddAction("Order", "Submit"));

        // Entity has an action "Submit" at entity level; also place one on stage
        var result = new DomainEvolution(original).Evolve()
            .AddActionToStage("Order", "Active", "Submit")
            .Apply();

        await Assert.That(result.Succeeded).IsTrue();
        var entity = result.Root.Types.OfType<Entity>().Single();

        // Entity-level action is untouched
        await Assert.That(entity.Actions.Count).IsEqualTo(1);
        await Assert.That(entity.Actions[0].Name).IsEqualTo("Submit");
        await Assert.That(entity.Actions[0].Effects).IsEmpty();

        // Stage gets its own new action (empty, same name)
        var stage = entity.Stages.Single();
        await Assert.That(stage.Actions.Count).IsEqualTo(1);
        await Assert.That(stage.Actions[0].Name).IsEqualTo("Submit");
        await Assert.That(stage.Actions[0].Effects).IsEmpty();
    }

    [Test]
    public async Task SuccessfulPropertyUpdate_StillWorks() {
        var original = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Order")
                .AddPropertyToEntity("Order", new Property("Name", new DomainTypeReference("Text"), [])));

        var result = new DomainEvolution(original)
            .Apply([new ChangePropertyTypeChange("Order", "Name",
                new DomainTypeReference("Text"))]);

        await Assert.That(result.Succeeded).IsTrue();
    }
}