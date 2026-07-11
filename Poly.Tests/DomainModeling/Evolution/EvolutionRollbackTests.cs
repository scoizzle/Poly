using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Bootstrap;
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

    // ── Documented behavior: silent-no-op on missing target ────────────────
    //
    // Adding a property/stage/action to a non-existent entity via
    // AddPropertyToEntityChange etc. currently completes without error.
    // The DomainChange.ApplyTo calls UpdateEntity which returns false
    // silently when the entity is not found, and the analysis gate does
    // not catch it because no structural/semantic error is produced.
    //
    // This means "add property to NonExistent" currently succeeds with
    // zero effect, not a rollback. This is a known gap:
    //   - DomainChange.ApplyTo should issue a diagnostic when UpdateEntity
    //     returns false (missing target)
    //   - The analyzer could catch dangling references at the entity level
    //
    // For M2, this is acceptable because the domain authoring tools
    // (add_entity → add_property chain) always target entities that exist.
    // Fix deferred to post-M2 analyzer hardening (WP9 or later).

    [Test]
    public async Task SilentNoOp_AddPropertyToMissingEntity_SucceedsWithNoEffect() {
        // Use an empty domain (no builtins) to clearly show no types were added
        var domain = new Domain("Test", [], []);
        var result = new DomainEvolution(domain).Evolve()
            .AddPropertyToEntity("NonExistent",
                new Property("X", new DomainTypeReference("Text"), []))
            .Apply();

        // This "succeeds" with no effect — the change is silently ignored
        // because DomainMutationContext.UpdateEntity returns false.
        // This is documented behavior; a future analyzer pass should fail-loud.
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root.Types.OfType<Entity>()).IsEmpty();
    }
}