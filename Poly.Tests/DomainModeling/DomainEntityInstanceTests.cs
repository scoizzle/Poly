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
}