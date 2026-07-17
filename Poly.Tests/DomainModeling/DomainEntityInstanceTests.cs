using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
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
        var entity = new Entity("Person", [age], Actions: [
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
        var entity = new Entity("Person", [age, active], Actions: [
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
        var entity = new Entity("Order", [status, total], Actions: [
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
        var entity = new Entity("Order", [status, total], Actions: [
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
    public async Task CreateEntityInstance_CreatesChildInstance() {
        var name = new Property("Name", new DomainTypeReference("Text"), []);
        var age = new Property("Age", new DomainTypeReference("Number"), []);
        var entity = new Entity("Person", [name, age], Actions: [
            new Poly.DomainModeling.Action("Spawn", InvocationResult.Void, [],
                Effects: [new CreateEntityInstance(
                    new DomainTypeReference("Person"),
                    Initializers: [
                        new PropertyBinding("Name", DomainExpression.Literal("Bob")),
                        new PropertyBinding("Age", DomainExpression.Literal(42L))
                    ])],
                Policies: [])
        ], [], []);

        var instance = DomainEntityInstance.Create(entity);
        var result = instance.CallAction("Spawn");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.CreatedChildren.Count).IsEqualTo(1);

        var child = instance.CreatedChildren[0];
        await Assert.That(child.Entity.Name).IsEqualTo("Person");
        await Assert.That(child.GetProperty<string>("Name")).IsEqualTo("Bob");
        await Assert.That(child.GetProperty<object>("Age")).IsEqualTo(42L);
    }

    [Test]
    public async Task CreateEntityInstance_MultipleChildren_AllCreated() {
        var entity = new Entity("Item", [], Actions: [
            new Poly.DomainModeling.Action("Batch", InvocationResult.Void, [],
                Effects: [
                    new CreateEntityInstance(new DomainTypeReference("Item")),
                    new CreateEntityInstance(new DomainTypeReference("Item")),
                    new CreateEntityInstance(new DomainTypeReference("Item"))
                ],
                Policies: [])
        ], [], []);

        var instance = DomainEntityInstance.Create(entity);
        instance.CallAction("Batch");

        await Assert.That(instance.CreatedChildren.Count).IsEqualTo(3);
    }

    [Test]
    public async Task CreateEntityInstance_SameType_WhenNoDomainReference() {
        var name = new Property("Name", new DomainTypeReference("Text"), []);
        var entity = new Entity("Person", [name], Actions: [
            new Poly.DomainModeling.Action("Clone", InvocationResult.Void, [],
                Effects: [new CreateEntityInstance(new DomainTypeReference("Person"),
                    Initializers: [new PropertyBinding("Name", DomainExpression.Literal("Clone"))])],
                Policies: [])
        ], [], []);

        var instance = DomainEntityInstance.Create(entity); // no domain reference
        instance.CallAction("Clone");

        await Assert.That(instance.CreatedChildren.Count).IsEqualTo(1);
        await Assert.That(instance.CreatedChildren[0].GetProperty<string>("Name")).IsEqualTo("Clone");
    }

    [Test]
    public async Task CreateEntityInstance_UsesDomainForCrossEntityLookup() {
        var personName = new Property("PersonName", new DomainTypeReference("Text"), []);
        var itemName = new Property("ItemName", new DomainTypeReference("Text"), []);
        var person = new Entity("Person", [personName], Actions: [
            new Poly.DomainModeling.Action("CreateItem", InvocationResult.Void, [],
                Effects: [new CreateEntityInstance(new DomainTypeReference("Item"),
                    Initializers: [new PropertyBinding("ItemName", DomainExpression.Literal("Widget"))])],
                Policies: [])
        ], [], []);
        var item = new Entity("Item", [itemName], [], [], []);
        var domain = new Domain("Test", [person, item], []);

        var instance = DomainEntityInstance.Create(person, domain: domain);
        instance.CallAction("CreateItem");

        await Assert.That(instance.CreatedChildren.Count).IsEqualTo(1);
        await Assert.That(instance.CreatedChildren[0].Entity.Name).IsEqualTo("Item");
        await Assert.That(instance.CreatedChildren[0].GetProperty<string>("ItemName")).IsEqualTo("Widget");
    }

    [Test]
    public async Task DeleteEntityInstance_SetsIsDeleted() {
        var entity = new Entity("Temp", [], Actions: [
            new Poly.DomainModeling.Action("Dispose", InvocationResult.Void, [],
                Effects: [new DeleteEntityInstance(new DomainTypeReference("Temp"))],
                Policies: [])
        ], [], []);

        var instance = DomainEntityInstance.Create(entity);
        await Assert.That(instance.IsDeleted).IsFalse();

        instance.CallAction("Dispose");
        await Assert.That(instance.IsDeleted).IsTrue();
    }



    [Test]
    public async Task InvokeActionEffect_ChainsToAnotherAction() {
        var count = new Property("Count", new DomainTypeReference("Number"), []);
        var entity = new Entity("Counter", [count], Actions: [
            new Poly.DomainModeling.Action("Increment", InvocationResult.Void, [],
                Effects: [new AssignEffect(
                    DomainExpression.Property("Count"),
                    DomainExpression.Add(DomainExpression.Property("Count"), DomainExpression.Literal(1L)))],
                Policies: []),
            new Poly.DomainModeling.Action("DoubleIncrement", InvocationResult.Void, [],
                Effects: [
                    new InvokeActionEffect("Increment", []),
                    new InvokeActionEffect("Increment", [])
                ],
                Policies: [])
        ], [], []);

        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Count"] = 0L });
        instance.CallAction("DoubleIncrement");

        await Assert.That(instance.GetProperty<object>("Count")).IsEqualTo(2L);
    }

    [Test]
    public async Task ActionWithMultipleEffects_ExecutesAllTypes() {
        var status = new Property("Status", new DomainTypeReference("Text"), []);
        var count = new Property("Count", new DomainTypeReference("Number"), []);
        var entity = new Entity("Worker", [status, count], Actions: [
            new Poly.DomainModeling.Action("DoAll", InvocationResult.Void, [],
                Effects: [
                    new AssignEffect(DomainExpression.Property("Status"),
                        DomainExpression.Literal("Started")),
                    new StageTransitionEffect(new StageReference("Active")),
                    new CreateEntityInstance(new DomainTypeReference("Worker"),
                        Initializers: [new PropertyBinding("Count", DomainExpression.Literal(0L))]),
                ],
                Policies: [])
        ], [], [new Stage("Draft", null, [], [], [], []), new Stage("Active", null, [], [], [], [])]);

        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Status"] = "", ["Count"] = 0L });
        var result = instance.CallAction("DoAll");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.CurrentStage).IsEqualTo("Active");
        await Assert.That(instance.GetProperty<string>("Status")).IsEqualTo("Started");
        await Assert.That(instance.CreatedChildren.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SubscriptionEffect_EventPropertyAccess_Gap_Documented() {
        // BR.1 / Option B: event.* property access in subscription effects
        // is NOT yet supported. The string-prefix convention (injecting
        // "event.{prop}" keys into _values) is not visible to the VM's
        // member resolution pipeline, which uses CLR type definitions.
        //
        // This test proves:
        //   1. The subscription DOES fire (initial Status is not null).
        //   2. The RHS "event.Code" DOES NOT resolve to the transitioning
        //      instance's value — it evaluates to null/default.
        //
        // When the DSL parser defines an "event" keyword and proper lowering
        // support is added, change this test to assert the expected value.
        var trackerStatus = new Property("Status", new DomainTypeReference("Text"), []);
        var tracker = new Entity("Tracker", [trackerStatus], [], [], [
            new Stage("Pending", null, [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("Status"),
                            DomainExpression.Property("event.Code"))
                    ])
                ]
            }
        ]);

        var orderCode = new Property("Code", new DomainTypeReference("Text"), []);
        var order = new Entity("Order", [orderCode], [
            new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", null, [], [], [], []),
            new Stage("Active", null, [], [], [], [])
        ]);

        var rel = new Relationship("Tracks",
            new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToOne, []);

        var domain = new Domain("Test", [tracker, order], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch)).IsFalse();

        var store = new DomainInstanceStore();
        var orderInstance = DomainEntityInstance.Create(order,
            new Dictionary<string, object?> { ["Code"] = "ABC-123" }, domain: domain);
        var trackerInstance = DomainEntityInstance.Create(tracker,
            new Dictionary<string, object?> { ["Status"] = "UNTOUCHED" }, domain: domain);
        store.Add(orderInstance);
        store.Add(trackerInstance);

        orderInstance.CallAction("Activate");

        // Subscription fires (Status was "UNTOUCHED", now it's "" because
        // AssignEffect wrote a default value), but "event.Code" does NOT
        // resolve to "ABC-123" through the VM's member resolution.
        // We prove both: subscription fired (Status changed from initial value)
        // AND the event.* reference did NOT work (Status != "ABC-123").
        var status = trackerInstance.GetProperty<string>("Status");
        await Assert.That(status).IsNotEqualTo("UNTOUCHED");  // subscription fired
        await Assert.That(status).IsNotEqualTo("ABC-123");     // but event.* didn't resolve
        // When event.* lowering is implemented, change to:
        //   await Assert.That(trackerInstance.GetProperty<string>("Status")).IsEqualTo("ABC-123");
    }
}