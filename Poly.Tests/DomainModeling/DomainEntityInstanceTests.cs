using Poly.DomainModeling;
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Effects;

namespace Poly.Tests.DomainModeling;

public class DomainEntityInstanceTests {
    private static Entity CreatePersonEntity() {
        var age = new Property("Age", new DomainTypeReference("Number"), []);
        var name = new Property("Name", new DomainTypeReference("Text"), []);
        var active = new Property("Active", new DomainTypeReference("Boolean"), []);

        var isAdult = new Policy("IsAdult",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"),
                DomainExpression.Literal(18L)));

        var isActive = new Policy("IsActive",
            DomainExpression.Equal(
                DomainExpression.Property("Active"),
                DomainExpression.Literal(true)));

        var activate = new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [],
            Effects: [new StageTransitionEffect(new StageReference("Active"))],
            Policies: [isActive]);

        var draft = new Stage("Draft", Parent: null, Actions: [activate],
            Policies: [], OnEntryEffects: [], OnExitEffects: []);

        var activeStage = new Stage("Active", Parent: null, Actions: [],
            Policies: [], OnEntryEffects: [], OnExitEffects: []);

        return new Entity("Person",
            Properties: [age, name, active],
            Events: [],
            Actions: [activate],
            Policies: [isAdult],
            Stages: [draft, activeStage]);
    }

    [Test]
    public async Task Create_Person_SetsPropertiesAndDefaults() {
        var entity = CreatePersonEntity();
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Name"] = "Alice", ["Age"] = 30L });

        await Assert.That(instance.GetProperty<string>("Name")).IsEqualTo("Alice");
        await Assert.That(instance.GetProperty<object>("Age")).IsEqualTo(30L);
        await Assert.That(instance.GetProperty<object>("Active")).IsNull(); // default
        await Assert.That(instance.CurrentStage).IsEqualTo("Draft"); // first stage
    }

    [Test]
    public async Task EvaluatePolicy_AgeGuard_ReturnsTrueForAdult() {
        var entity = CreatePersonEntity();
        var adult = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Age"] = 25L });
        var minor = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Age"] = 15L });

        await Assert.That(adult.EvaluatePolicy(entity.Policies.First(p => p.Name == "IsAdult"))).IsTrue();
        await Assert.That(minor.EvaluatePolicy(entity.Policies.First(p => p.Name == "IsAdult"))).IsFalse();
    }

    [Test]
    public async Task EvaluatePolicy_CoercesIntToLong() {
        var entity = CreatePersonEntity();
        // Store Age as int — coercion should handle it
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Age"] = 30 });

        await Assert.That(instance.EvaluatePolicy(
            entity.Policies.First(p => p.Name == "IsAdult"))).IsTrue();
    }

    [Test]
    public async Task CallAction_WithPassingGuards_Succeeds() {
        var entity = CreatePersonEntity();
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Active"] = true, ["Age"] = 25L });

        var result = instance.CallAction("Activate");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.NewStage).IsEqualTo("Active");
        await Assert.That(instance.CurrentStage).IsEqualTo("Active");
    }

    [Test]
    public async Task CallAction_WithFailingGuard_Fails() {
        var entity = CreatePersonEntity();
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Active"] = false, ["Age"] = 25L });

        var result = instance.CallAction("Activate");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailedGuards).Contains("IsActive");
        await Assert.That(instance.CurrentStage).IsEqualTo("Draft"); // unchanged
    }

    [Test]
    public async Task CallAction_UnknownAction_ReturnsNotFound() {
        var entity = CreatePersonEntity();
        var instance = DomainEntityInstance.Create(entity);

        var result = instance.CallAction("NonExistent");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("not found");
    }

    [Test]
    public async Task Create_UnknownProperty_Throws() {
        var entity = CreatePersonEntity();

        await Assert.ThrowsAsync<ArgumentException>(() => {
            DomainEntityInstance.Create(entity,
                new Dictionary<string, object?> { ["NonExistent"] = 42 });
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task SetProperty_UpdatesValue() {
        var entity = CreatePersonEntity();
        var instance = DomainEntityInstance.Create(entity);

        instance.SetProperty("Age", 42L);
        await Assert.That(instance.GetProperty<object>("Age")).IsEqualTo(42L);
    }

    [Test]
    public async Task EvaluatePolicy_EntityLevelPolicy_EvaluatesCorrectly() {
        // Entity-level policy (IsAdult on the entity, not on an action)
        var entity = CreatePersonEntity();
        var adult = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Age"] = 21L });

        // IsAdult is at entity level
        var policy = entity.Policies.First(p => p.Name == "IsAdult");
        await Assert.That(adult.EvaluatePolicy(policy)).IsTrue();
    }

    // ── Slice 4: Effect execution ──────────────────────────────

    [Test]
    public async Task AssignEffect_UpdatesPropertyViaVm() {
        var age = new Property("Age", new DomainTypeReference("Number"), []);
        var entity = new Entity("Person", [age], [], Actions: [
            new Poly.DomainModeling.Action("SetAge", InvocationResult.Void, [],
                Effects: [new AssignEffect(
                    DomainExpression.Property("Age"),
                    DomainExpression.Literal(42L))],
                Policies: [])
        ], [], []);

        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Age"] = 0L });

        var result = instance.CallAction("SetAge");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.GetProperty<object>("Age")).IsEqualTo(42L);
    }

    [Test]
    public async Task CompositeEffect_ExecutesAllSubEffects() {
        var age = new Property("Age", new DomainTypeReference("Number"), []);
        var active = new Property("Active", new DomainTypeReference("Boolean"), []);
        var entity = new Entity("Person", [age, active], [], Actions: [
            new Poly.DomainModeling.Action("Setup", InvocationResult.Void, [],
                Effects: [new CompositeEffect([
                    new AssignEffect(DomainExpression.Property("Age"), DomainExpression.Literal(30L)),
                    new AssignEffect(DomainExpression.Property("Active"), DomainExpression.Literal(true))
                ])],
                Policies: [])
        ], [], []);

        var instance = DomainEntityInstance.Create(entity);
        var result = instance.CallAction("Setup");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.GetProperty<object>("Age")).IsEqualTo(30L);
        await Assert.That(instance.GetProperty<object>("Active")).IsTypeOf<bool>();
        await Assert.That(instance.GetProperty<bool>("Active")).IsTrue();
    }

    [Test]
    public async Task ConditionalEffect_WhenConditionTrue_ExecutesThenBranch() {
        var status = new Property("Status", new DomainTypeReference("Text"), []);
        var total = new Property("Total", new DomainTypeReference("Number"), []);
        var entity = new Entity("Order", [status, total], [], Actions: [
            new Poly.DomainModeling.Action("Process", InvocationResult.Void, [],
                Effects: [new ConditionalEffect(
                    Condition: DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("Total"),
                        DomainExpression.Literal(100L)),
                    ThenEffects: [new AssignEffect(
                        DomainExpression.Property("Status"),
                        DomainExpression.Literal("Approved"))],
                    ElseEffects: [new AssignEffect(
                        DomainExpression.Property("Status"),
                        DomainExpression.Literal("Review"))]
                )],
                Policies: [])
        ], [], []);

        var big = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Total"] = 200L });
        big.CallAction("Process");
        await Assert.That(big.GetProperty<string>("Status")).IsEqualTo("Approved");

        var small = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Total"] = 50L });
        small.CallAction("Process");
        await Assert.That(small.GetProperty<string>("Status")).IsEqualTo("Review");
    }

    [Test]
    public async Task ConditionalEffect_WithoutElse_NoopsWhenFalse() {
        var status = new Property("Status", new DomainTypeReference("Text"), []);
        var total = new Property("Total", new DomainTypeReference("Number"), []);
        var entity = new Entity("Order", [status, total], [], Actions: [
            new Poly.DomainModeling.Action("FlagLarge", InvocationResult.Void, [],
                Effects: [new ConditionalEffect(
                    Condition: DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("Total"),
                        DomainExpression.Literal(1000L)),
                    ThenEffects: [new AssignEffect(
                        DomainExpression.Property("Status"),
                        DomainExpression.Literal("Flagged"))],
                    ElseEffects: null
                )],
                Policies: [])
        ], [], []);

        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Total"] = 50L, ["Status"] = "OK" });
        instance.CallAction("FlagLarge");
        await Assert.That(instance.GetProperty<string>("Status")).IsEqualTo("OK");
    }

    [Test]
    public async Task CreateEntityInstance_InitializesProperties() {
        var name = new Property("Name", new DomainTypeReference("Text"), []);
        var age = new Property("Age", new DomainTypeReference("Number"), []);
        var entity = new Entity("Person", [name, age], [], Actions: [
            new Poly.DomainModeling.Action("Initialize", InvocationResult.Void, [],
                Effects: [new CreateEntityInstance(
                    new DomainTypeReference("Person"),
                    Initializers: [
                        new PropertyBinding("Name", DomainExpression.Literal("Bob")),
                        new PropertyBinding("Age", DomainExpression.Literal(42L))
                    ])],
                Policies: [])
        ], [], []);

        var instance = DomainEntityInstance.Create(entity);
        var result = instance.CallAction("Initialize");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.GetProperty<string>("Name")).IsEqualTo("Bob");
        await Assert.That(instance.GetProperty<object>("Age")).IsEqualTo(42L);
    }
}