using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;

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

        var draft = new Stage("Draft",
            Actions: [activate],
            Policies: [], OnEntryEffects: [], OnExitEffects: []);
        var activeStage = new Stage("Active",
            Actions: [],
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
    public async Task InvokeAction_WithPassingGuards_Succeeds() {
        var entity = CreatePersonEntity();
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Active"] = true, ["Age"] = 25L });

        var result = instance.InvokeAction("Activate");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.NewStage).IsEqualTo("Active");
        await Assert.That(instance.CurrentStage).IsEqualTo("Active");
    }

    [Test]
    public async Task InvokeAction_WithFailingGuard_Fails() {
        var entity = CreatePersonEntity();
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Active"] = false, ["Age"] = 25L });

        var result = instance.InvokeAction("Activate");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailedGuards).Contains("IsActive");
        await Assert.That(instance.CurrentStage).IsEqualTo("Draft"); // unchanged
    }

    [Test]
    public async Task InvokeAction_UnknownAction_ReturnsNotFound() {
        var entity = CreatePersonEntity();
        var instance = DomainEntityInstance.Create(entity);

        var result = instance.InvokeAction("NonExistent");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("not found");
    }

    [Test]
    public async Task InvokeAction_Standalone_EmptyStageCopyWithParams_FallsThroughToEntityAction() {
        // Standalone reduced contract (Domain null): structural SA fallthrough must
        // match catalog semantics. Params-carrying empty stage-copy → entity action.
        var entityAction = new Poly.DomainModeling.Action("Submit", InvocationResult.Void,
            Parameters: [new Property("Note", new DomainTypeReference("Text"), [])],
            Effects: [new StageTransitionEffect(new StageReference("Active"))],
            Policies: []);
        var draft = new Stage("Draft",
            Actions: [new Poly.DomainModeling.Action("Submit", InvocationResult.Void,
                Parameters: [new Property("Note", new DomainTypeReference("Text"), [])],
                Effects: [], Policies: [])],
            Policies: [], OnEntryEffects: [], OnExitEffects: []);
        var active = new Stage("Active", Actions: [], Policies: [], OnEntryEffects: [], OnExitEffects: []);
        var entity = new Entity("Order",
            Properties: [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [entityAction], Policies: [], Stages: [draft, active]);

        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?>());

        await Assert.That(instance.Domain).IsNull();
        var result = instance.InvokeAction("Submit");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.NewStage).IsEqualTo("Active");
        await Assert.That(instance.CurrentStage).IsEqualTo("Active");
    }

    [Test]
    public async Task InvokeAction_DomainBound_EmptyStageCopy_UsesCatalogFallthrough() {
        var entityAction = new Poly.DomainModeling.Action("Submit", InvocationResult.Void, [],
            [new StageTransitionEffect(new StageReference("Active"))], []);
        var draft = new Stage("Draft",
            [new Poly.DomainModeling.Action("Submit", InvocationResult.Void, [], [], [])],
            [], [], []);
        var active = new Stage("Active", [], [], [], []);
        var order = new Entity("Order",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [entityAction], Policies: [], Stages: [draft, active]);
        var domain = new Domain("OrderDomain", [order], []);

        var instance = DomainEntityInstance.Create(order,
            new Dictionary<string, object?>(), domain);

        await Assert.That(instance.Domain).IsNotNull();
        var result = instance.InvokeAction("Submit");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.NewStage).IsEqualTo("Active");
        await Assert.That(instance.CurrentStage).IsEqualTo("Active");
    }

    [Test]
    public async Task InvokeAction_DomainBound_Throws_WhenCatalogMissing() {
        var entityAction = new Poly.DomainModeling.Action("Submit", InvocationResult.Void, [],
            [new StageTransitionEffect(new StageReference("Active"))], []);
        var draft = new Stage("Draft",
            [new Poly.DomainModeling.Action("Submit", InvocationResult.Void, [], [], [])],
            [], [], []);
        var active = new Stage("Active", [], [], [], []);
        var order = new Entity("Order",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [entityAction], Policies: [], Stages: [draft, active]);
        var domain = new Domain("OrderDomain", [order], []);

        var instance = DomainEntityInstance.Create(order,
            new Dictionary<string, object?>(), domain);
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        analysis.GetMetadataStore().Remove<DomainCatalogMetadata>(domain);

        var ex = Assert.Throws<InvalidOperationException>(() => instance.InvokeAction("Submit"));
        await Assert.That(ex!.Message).Contains("DomainCatalogMetadata");
    }

    [Test]
    public async Task InvokeAction_DomainBound_UnknownAction_ReturnsMissing_WhenCatalogPresent() {
        var order = new Entity("Order",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [], Policies: [],
            Stages: [new Stage("Draft", [], [], [], [])]);
        var domain = new Domain("OrderDomain", [order], []);
        var instance = DomainEntityInstance.Create(order, domain: domain);

        var result = instance.InvokeAction("DoesNotExist");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("not found");
    }

    [Test]
    public async Task InvokeAction_Standalone_CreateInRelationship_Unsupported() {
        // Standalone reduced contract: relationship semantic resolve requires Domain.
        var createIn = new Poly.DomainModeling.Action("Spawn", InvocationResult.Void, [],
            [new CreateEntityInRelationshipEffect("Owns", [])], []);
        var entity = new Entity("Parent",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [createIn], Policies: [], Stages: []);
        var instance = DomainEntityInstance.Create(entity);

        await Assert.That(instance.Domain).IsNull();
        var ex = Assert.Throws<InvalidOperationException>(() => instance.InvokeAction("Spawn"));
        await Assert.That(ex!.Message).Contains("domain");
    }

    [Test]
    public async Task TransitionStage_Reentrancy_ExceedsMaxDepth_Throws_Standalone() {
        // Standalone sibling: Entity.Stages scan path.
        var stages = new List<Stage>();
        for (var i = DomainEntityInstance.MaxTransitionDepth + 2; i >= 0; i--) {
            var onEntry = i == DomainEntityInstance.MaxTransitionDepth + 2
                ? Array.Empty<Effect>()
                : new Effect[] { new StageTransitionEffect(new StageReference($"S{i + 1}")) };
            stages.Insert(0, new Stage($"S{i}", [], [], onEntry, []));
        }

        var entity = new Entity("Loop",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [], Policies: [], Stages: stages);
        var instance = DomainEntityInstance.Create(entity);

        var ex = Assert.Throws<InvalidOperationException>(() => instance.TransitionStage("S1"));
        await Assert.That(ex!.Message).Contains("re-entrancy");
        await Assert.That(ex.Message).Contains(DomainEntityInstance.MaxTransitionDepth.ToString());
    }

    [Test]
    public async Task TransitionStage_Reentrancy_ExceedsMaxDepth_Throws_DomainBound() {
        // Domain-bound sibling: catalog + TryGetStage + analysis-aware lowering (Q6).
        var stages = new List<Stage>();
        for (var i = DomainEntityInstance.MaxTransitionDepth + 2; i >= 0; i--) {
            var onEntry = i == DomainEntityInstance.MaxTransitionDepth + 2
                ? Array.Empty<Effect>()
                : new Effect[] { new StageTransitionEffect(new StageReference($"S{i + 1}")) };
            stages.Insert(0, new Stage($"S{i}", [], [], onEntry, []));
        }

        var entity = new Entity("Loop",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [], Policies: [], Stages: stages);
        var domain = new Domain("LoopDomain", [entity], []);
        var instance = DomainEntityInstance.Create(entity, domain: domain);

        var ex = Assert.Throws<InvalidOperationException>(() => instance.TransitionStage("S1"));
        await Assert.That(ex!.Message).Contains("re-entrancy");
        await Assert.That(ex.Message).Contains(DomainEntityInstance.MaxTransitionDepth.ToString());
    }

    [Test]
    public async Task AnalyzeRequiringCatalog_ProducesCatalog_ForValidDomain() {
        var order = new Entity("Order",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [], Policies: [],
            Stages: [new Stage("Draft", [], [], [], [])]);
        var domain = new Domain("Orders", [order], []);

        var analysis = DomainModelAnalyzer.AnalyzeRequiringCatalog(domain);

        await Assert.That(analysis.GetCatalog(domain)).IsNotNull();
        await Assert.That(analysis.GetCatalog(domain)!.ActionsByEntityName.ContainsKey("Order")).IsTrue();
    }

    [Test]
    public async Task RequireCatalog_Throws_WhenCatalogMissingWithoutStructuralFailure() {
        // Success tree with catalog stripped — RequireCatalog throw branch (Q3).
        // Full analyze is required so HasStructuralFailure is false; partial Semantic-only
        // hosts still structural-fail on unknown built-in type names without bootstrap.
        var domain = DomainFactory.Create("Orders", b =>
            b.AddEntity("Order")
             .AddPropertyToEntity("Order", new Property("Name", new DomainTypeReference("Text"), []))
             .AddStage("Order", "Draft"));

        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.HasStructuralFailure).IsFalse();
        await Assert.That(analysis.GetCatalog(domain)).IsNotNull();

        analysis.GetMetadataStore().Remove<DomainCatalogMetadata>(domain);
        await Assert.That(analysis.GetCatalog(domain)).IsNull();

        var ex = Assert.Throws<InvalidOperationException>(
            () => DomainModelAnalyzer.RequireCatalog(analysis, domain));
        await Assert.That(ex!.Message).Contains("DomainCatalogMetadata");
        await Assert.That(ex.Message).Contains(domain.Name);
    }

    [Test]
    public async Task RequireCatalog_Returns_WhenStructuralFailureWithoutCatalog() {
        // CatalogPass without Semantic bags → structural failure, no catalog (Q2/Q3).
        // Invoke pass directly; AnalyzerBuilder rejects CatalogPass without its dep registered.
        var domain = new Domain("EmptyStructural", [], []);
        var context = AnalysisContext.CreateDefault();
        new DomainCatalogPass().Analyze(context, domain);
        var analysis = new AnalysisResult(context, AnalysisTelemetry.Empty);

        await Assert.That(analysis.HasStructuralFailure).IsTrue();
        await Assert.That(analysis.GetCatalog(domain)).IsNull();

        // Must not throw — structural failures may omit catalog.
        DomainModelAnalyzer.RequireCatalog(analysis, domain);
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

        var result = instance.InvokeAction("SetAge");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.GetProperty<object>("Age")).IsEqualTo(42L);
    }

    [Test]
    public async Task AssignEffect_FromActionParameter_UpdatesProperty() {
        var label = new Property("Label", new DomainTypeReference("Text"), []);
        var valueParam = new Property("value", new DomainTypeReference("Text"), []);
        var entity = new Entity("Item", [label], Actions: [
            new Poly.DomainModeling.Action("Tag", InvocationResult.Void,
                Parameters: [valueParam],
                Effects: [new AssignEffect(
                    DomainExpression.Property("Label"),
                    DomainExpression.Property("value"))],
                Policies: [])
        ], [], []);

        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Label"] = "unset" });

        var result = instance.InvokeAction("Tag",
            new Dictionary<string, object?> { ["value"] = "tagged" });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.GetProperty<object>("Label")).IsEqualTo("tagged");
        // Arg must not leak into the property bag after the call.
        await Assert.That(instance.Snapshot().ContainsKey("value")).IsFalse();
    }

    [Test]
    public async Task AssignEffect_FromNestedInvokeArgs_UpdatesProperty() {
        var label = new Property("Label", new DomainTypeReference("Text"), []);
        var valueParam = new Property("value", new DomainTypeReference("Text"), []);
        var entity = new Entity("Item", [label], Actions: [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [],
                Effects: [new InvokeActionEffect("Apply", [
                    new PropertyBinding("value", DomainExpression.Literal("from-invoke"))
                ])],
                Policies: []),
            new Poly.DomainModeling.Action("Apply", InvocationResult.Void,
                Parameters: [valueParam],
                Effects: [new AssignEffect(
                    DomainExpression.Property("Label"),
                    DomainExpression.Property("value"))],
                Policies: [])
        ], [], []);

        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Label"] = "before" });

        var result = instance.InvokeAction("Go");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.GetProperty<object>("Label")).IsEqualTo("from-invoke");
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
        var result = instance.InvokeAction("Setup");

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
        big.InvokeAction("Process");
        await Assert.That(big.GetProperty<string>("Status")).IsEqualTo("Approved");

        var small = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Total"] = 50L });
        small.InvokeAction("Process");
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
        instance.InvokeAction("FlagLarge");
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
        var result = instance.InvokeAction("Spawn");

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
        instance.InvokeAction("Batch");

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
        instance.InvokeAction("Clone");

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
        instance.InvokeAction("CreateItem");

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

        instance.InvokeAction("Dispose");
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
        instance.InvokeAction("DoubleIncrement");

        await Assert.That(instance.GetProperty<object>("Count")).IsEqualTo(2L);
    }

    [Test]
    public async Task InvokeActionEffect_CrossEntity_InvokesOnRelatedInstance() {
        // E3b: invoke RelName.ActionName(args) — resolve target via store relationship
        var status = new Property("Status", new DomainTypeReference("Text"), []);
        var count = new Property("Count", new DomainTypeReference("Number"), []);

        // Target entity: Service has an action we invoke
        var service = new Entity("Service", [status, count], Actions: [
            new Poly.DomainModeling.Action("Process", InvocationResult.Void, [], [
                new AssignEffect(DomainExpression.Property("Status"),
                    DomainExpression.Literal("processed"))
            ], [])
        ], [], []);

        // Source entity: Orchestrator has an action that invokes service.Process
        var orchestrator = new Entity("Orchestrator", [count], Actions: [
            new Poly.DomainModeling.Action("Run", InvocationResult.Void, [], [
                new InvokeActionEffect("Process", [], TargetRelationship: "ServiceCall")
            ], [])
        ], [], []);

        var rel = new Relationship("ServiceCall",
            new DomainTypeReference("Orchestrator"), new DomainTypeReference("Service"),
            RelationshipCardinality.OneToOne, []);

        var domain = new Domain("Test", [orchestrator, service], [rel]);

        var store = new DomainInstanceStore();
        var svc = DomainEntityInstance.Create(service,
            new Dictionary<string, object?> { ["Status"] = "idle", ["Count"] = 0L },
            domain: domain);
        var orch = DomainEntityInstance.Create(orchestrator,
            new Dictionary<string, object?> { ["Count"] = 1L },
            domain: domain);
        store.Add(svc);
        store.Add(orch);
        store.Link("ServiceCall", orch, svc);

        var result = orch.InvokeAction("Run");
        await Assert.That(result.Succeeded).IsTrue();
        // Service instance should have been modified by the cross-entity invoke
        await Assert.That(svc.GetProperty<object>("Status")).IsEqualTo("processed");
    }

    [Test]
    public async Task InvokeActionEffect_CrossEntity_WithArgs() {
        // E3b with args passed through the relationship
        var label = new Property("Label", new DomainTypeReference("Text"), []);

        var target = new Entity("Target", [label], Actions: [
            new Poly.DomainModeling.Action("SetText", InvocationResult.Void,
                Parameters: [new Property("msg", new DomainTypeReference("Text"), [])],
                Effects: [new AssignEffect(
                    DomainExpression.Property("Label"),
                    DomainExpression.Property("msg"))],
                Policies: [])
        ], [], []);

        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new InvokeActionEffect("SetText", [
                    new PropertyBinding("msg", DomainExpression.Literal("cross-entity!"))
                ], TargetRelationship: "Link")
            ], [])
        ], [], []);

        var rel = new Relationship("Link",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToOne, []);
        var domain = new Domain("Test", [source, target], [rel]);

        var store = new DomainInstanceStore();
        var tgt = DomainEntityInstance.Create(target,
            new Dictionary<string, object?> { ["Label"] = "unset" }, domain: domain);
        var src = DomainEntityInstance.Create(source, domain: domain);
        store.Add(tgt);
        store.Add(src);
        store.Link("Link", src, tgt);

        var result = src.InvokeAction("Go");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(tgt.GetProperty<object>("Label")).IsEqualTo("cross-entity!");
    }

    // ── E3b quantifier + filter tests ──────────────────────────

    [Test]
    public async Task InvokeActionEffect_CrossEntity_All_InvokesOnEveryTarget() {
        var status = new Property("Status", new DomainTypeReference("Text"), []);
        var target = new Entity("Target", [status], Actions: [
            new Poly.DomainModeling.Action("Process", InvocationResult.Void, [], [
                new AssignEffect(DomainExpression.Property("Status"),
                    DomainExpression.Literal("done"))
            ], [])
        ], [], []);

        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Action("RunAll", InvocationResult.Void, [], [
                new InvokeActionEffect("Process", [],
                    TargetRelationship: "Items",
                    Quantifier: StageSubscriptionQuantifier.All)
            ], [])
        ], [], []);

        var rel = new Relationship("Items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [source, target], [rel]);

        var store = new DomainInstanceStore();
        var tgt1 = DomainEntityInstance.Create(target,
            new Dictionary<string, object?> { ["Status"] = "a" }, domain: domain);
        var tgt2 = DomainEntityInstance.Create(target,
            new Dictionary<string, object?> { ["Status"] = "b" }, domain: domain);
        var src = DomainEntityInstance.Create(source, domain: domain);
        store.Add(tgt1); store.Add(tgt2); store.Add(src);
        store.Link("Items", src, tgt1);
        store.Link("Items", src, tgt2);

        var result = src.InvokeAction("RunAll");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(tgt1.GetProperty<object>("Status")).IsEqualTo("done");
        await Assert.That(tgt2.GetProperty<object>("Status")).IsEqualTo("done");
    }

    [Test]
    public async Task InvokeActionEffect_CrossEntity_Any_WithFilter() {
        var status = new Property("Status", new DomainTypeReference("Text"), []);
        var count = new Property("Count", new DomainTypeReference("Number"), []);

        var target = new Entity("Target", [status, count], Actions: [
            new Poly.DomainModeling.Action("Process", InvocationResult.Void, [], [
                new AssignEffect(DomainExpression.Property("Status"),
                    DomainExpression.Literal("processed"))
            ], [])
        ], [], []);

        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Action("RunAny", InvocationResult.Void, [], [
                new InvokeActionEffect("Process", [],
                    TargetRelationship: "Items",
                    Quantifier: StageSubscriptionQuantifier.Any,
                    Filter: DomainExpression.Equal(
                        DomainExpression.Property("Count"),
                        DomainExpression.Literal(42L)))
            ], [])
        ], [], []);

        var rel = new Relationship("Items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [source, target], [rel]);

        var store = new DomainInstanceStore();
        var tgt1 = DomainEntityInstance.Create(target,
            new Dictionary<string, object?> { ["Status"] = "a", ["Count"] = 1L }, domain: domain);
        var tgt2 = DomainEntityInstance.Create(target,
            new Dictionary<string, object?> { ["Status"] = "b", ["Count"] = 42L }, domain: domain);
        var tgt3 = DomainEntityInstance.Create(target,
            new Dictionary<string, object?> { ["Status"] = "c", ["Count"] = 99L }, domain: domain);
        var src = DomainEntityInstance.Create(source, domain: domain);
        store.Add(tgt1); store.Add(tgt2); store.Add(tgt3); store.Add(src);
        store.Link("Items", src, tgt1);
        store.Link("Items", src, tgt2);
        store.Link("Items", src, tgt3);

        var result = src.InvokeAction("RunAny");
        await Assert.That(result.Succeeded).IsTrue();
        // Only tgt2 (Count=42) should have been processed; tgt1 and tgt3 unchanged
        await Assert.That(tgt2.GetProperty<object>("Status")).IsEqualTo("processed");
        await Assert.That(tgt1.GetProperty<object>("Status")).IsEqualTo("a");
        await Assert.That(tgt3.GetProperty<object>("Status")).IsEqualTo("c");
    }

    [Test]
    public async Task InvokeActionEffect_CrossEntity_All_EmptyTargets_Throws() {
        // Fail-closed: vacuous all is not success.
        var status = new Property("Status", new DomainTypeReference("Text"), []);
        var target = new Entity("Target", [status], Actions: [
            new Poly.DomainModeling.Action("Process", InvocationResult.Void, [], [
                new AssignEffect(DomainExpression.Property("Status"),
                    DomainExpression.Literal("done"))
            ], [])
        ], [], []);

        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Action("RunAll", InvocationResult.Void, [], [
                new InvokeActionEffect("Process", [],
                    TargetRelationship: "Items",
                    Quantifier: StageSubscriptionQuantifier.All)
            ], [])
        ], [], []);

        var rel = new Relationship("Items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var store = new DomainInstanceStore();
        var src = DomainEntityInstance.Create(source, domain: domain);
        store.Add(src);

        await Assert.That(() => src.InvokeAction("RunAll")).Throws<InvalidOperationException>();
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
        ], [], [new Stage("Draft", [], [], [], []), new Stage("Active", [], [], [], [])]);

        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Status"] = "", ["Count"] = 0L });
        var result = instance.InvokeAction("DoAll");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.CurrentStage).IsEqualTo("Active");
        await Assert.That(instance.GetProperty<string>("Status")).IsEqualTo("Started");
        await Assert.That(instance.CreatedChildren.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SubscriptionEffect_PeerBinding_CopiesPeerProperty() {
        // when Tracks Active as order { assign Status to order Code }
        var trackerStatus = new Property("Status", new DomainTypeReference("Text"), []);
        var tracker = new Entity("Tracker", [trackerStatus], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription(
                        RelationshipName: "Tracks",
                        StageNames: ["Active"],
                        Quantifier: StageSubscriptionQuantifier.Each,
                        Effects: [
                            new AssignEffect(
                                DomainExpression.Property("Status"),
                                DomainExpression.RelationshipNav("order",
                                    DomainExpression.Property("Code")))
                        ],
                        PeerBinding: "order")
                ]
            }
        ]);

        var orderCode = new Property("Code", new DomainTypeReference("Text"), []);
        var order = new Entity("Order", [orderCode], [
            new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var rel = new Relationship("Tracks",
            new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToOne, []);

        var domain = new Domain("Test", [tracker, order], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionEffectBinding
            && d.Severity == DiagnosticSeverity.Error)).IsFalse();

        var store = new DomainInstanceStore();
        var orderInstance = DomainEntityInstance.Create(order,
            new Dictionary<string, object?> { ["Code"] = "ABC-123" }, domain: domain);
        var trackerInstance = DomainEntityInstance.Create(tracker,
            new Dictionary<string, object?> { ["Status"] = "UNTOUCHED" }, domain: domain);
        store.Add(orderInstance);
        store.Add(trackerInstance);
        store.Link("Tracks", trackerInstance, orderInstance);

        orderInstance.InvokeAction("Activate");

        await Assert.That(trackerInstance.GetProperty<string>("Status")).IsEqualTo("ABC-123");
    }

    [Test]
    public async Task SubscriptionEffect_NotificationOnly_DoesNotRequirePeerBinding() {
        var trackerStatus = new Property("Status", new DomainTypeReference("Text"), []);
        var tracker = new Entity("Tracker", [trackerStatus], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("Status"),
                            DomainExpression.Literal("NOTIFIED"))
                    ])
                ]
            }
        ]);

        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [
            new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var rel = new Relationship("Tracks",
            new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToOne, []);
        var domain = new Domain("Test", [tracker, order], [rel]);
        var store = new DomainInstanceStore();
        var orderInstance = DomainEntityInstance.Create(order,
            new Dictionary<string, object?> { ["Code"] = "X" }, domain: domain);
        var trackerInstance = DomainEntityInstance.Create(tracker,
            new Dictionary<string, object?> { ["Status"] = "IDLE" }, domain: domain);
        store.Add(orderInstance);
        store.Add(trackerInstance);
        store.Link("Tracks", trackerInstance, orderInstance);

        orderInstance.InvokeAction("Activate");

        await Assert.That(trackerInstance.GetProperty<string>("Status")).IsEqualTo("NOTIFIED");
    }

    [Test]
    public async Task ExecuteSubscriptionEffects_DomainBound_UsesAnalysisAwareLowering() {
        // Q5: domain-bound subscription effects must lower with analysis/domain context
        // (same path as TransitionStage OnEntry/OnExit). Assign still fires.
        var trackerStatus = new Property("Status", new DomainTypeReference("Text"), []);
        var tracker = new Entity("Tracker", [trackerStatus], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("Status"),
                            DomainExpression.Literal("NOTIFIED"))
                    ])
                ]
            }
        ]);

        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [
            new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var tracks = new Relationship("Tracks",
            new DomainTypeReference("Tracker"),
            new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("SubDomain", [order, tracker], [tracks]);
        var store = new DomainInstanceStore();
        var orderInstance = DomainEntityInstance.Create(order,
            new Dictionary<string, object?> { ["Code"] = "X" }, domain);
        var trackerInstance = DomainEntityInstance.Create(tracker,
            new Dictionary<string, object?> { ["Status"] = "IDLE" }, domain);
        store.Add(orderInstance);
        store.Add(trackerInstance);
        store.Link("Tracks", trackerInstance, orderInstance);

        orderInstance.InvokeAction("Activate");

        await Assert.That(trackerInstance.GetProperty<string>("Status")).IsEqualTo("NOTIFIED");
    }

    [Test]
    public async Task ExecuteSubscriptionEffects_Exception_ClearsSubscriptionFlag_AllowsRetry() {
        // If a subscription effect throws, _isExecutingSubscription must clear so a later
        // transition can fire subscriptions again (F8 — retired event.* bag oracle).
        var trackerStatus = new Property("Status", new DomainTypeReference("Text"), []);
        var tracker = new Entity("Tracker", [trackerStatus], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("NonexistentProp"),
                            DomainExpression.Literal("will fail"))
                    ])
                ]
            }
        ]);

        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [
            new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var rel = new Relationship("Tracks",
            new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToOne, []);

        var domain = new Domain("Test", [tracker, order], [rel]);

        var store = new DomainInstanceStore();
        var orderInstance = DomainEntityInstance.Create(order,
            new Dictionary<string, object?> { ["Code"] = "ABC-123" }, domain: domain);
        var trackerInstance = DomainEntityInstance.Create(tracker,
            new Dictionary<string, object?> { ["Status"] = "UNTOUCHED" }, domain: domain);
        store.Add(orderInstance);
        store.Add(trackerInstance);
        store.Link("Tracks", trackerInstance, orderInstance);

        var threw = false;
        try {
            orderInstance.InvokeAction("Activate");
        }
        catch {
            threw = true;
        }
        await Assert.That(threw).IsTrue();
        await Assert.That(trackerInstance.GetProperty<string>("Status")).IsEqualTo("UNTOUCHED");

        // Flag cleared: a second linked subscriber still receives the next notify (and throws).
        var freshTracker = DomainEntityInstance.Create(tracker,
            new Dictionary<string, object?> { ["Status"] = "FRESH" }, domain: domain);
        store.Add(freshTracker);

        var order2 = DomainEntityInstance.Create(order,
            new Dictionary<string, object?> { ["Code"] = "DEF-456" }, domain: domain);
        store.Add(order2);
        store.Link("Tracks", freshTracker, order2);

        threw = false;
        try {
            order2.InvokeAction("Activate");
        }
        catch {
            threw = true;
        }
        await Assert.That(threw).IsTrue();
        await Assert.That(freshTracker.GetProperty<string>("Status")).IsEqualTo("FRESH");
    }

    [Test]
    public async Task SubscriptionEffect_PathPrefixWithoutPeerBinding_AnalysisError() {
        // F1: unbound peer-like root without `as name` fails closed at analysis.
        var tracker = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("Status"),
                            DomainExpression.RelationshipNav("order",
                                DomainExpression.Property("Code")))
                    ])
                ]
            }
        ]);
        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);
        var rel = new Relationship("Tracks",
            new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToOne, []);
        var domain = new Domain("Test", [tracker, order], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionEffectBinding
            && d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("as name", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task SubscriptionEffect_LegacyEventRoot_AnalysisError() {
        var tracker = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("Status"),
                            DomainExpression.RelationshipNav("event",
                                DomainExpression.Property("Code")))
                    ])
                ]
            }
        ]);
        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);
        var rel = new Relationship("Tracks",
            new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToOne, []);
        var domain = new Domain("Test", [tracker, order], [rel]);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionEffectBinding
            && d.Message.Contains("event", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task RuntimeDispatchPlan_CarriesPeerBinding() {
        var tracker = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription(
                        RelationshipName: "Tracks",
                        StageNames: ["Active"],
                        Quantifier: StageSubscriptionQuantifier.Each,
                        Effects: [
                            new AssignEffect(
                                DomainExpression.Property("Status"),
                                DomainExpression.RelationshipNav("order",
                                    DomainExpression.Property("Code")))
                        ],
                        PeerBinding: "order")
                ]
            }
        ]);
        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);
        var domain = new Domain("Test", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var pending = tracker.Stages[0];
        var plan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(pending);
        await Assert.That(plan).IsNotNull();
        await Assert.That(plan!.ByRelationshipName.TryGetValue("Tracks", out var entries)).IsTrue();
        await Assert.That(entries![0].PeerBinding).IsEqualTo("order");
    }

    [Test]
    public async Task EntityLevelSubscription_AnalysisAccepts_WhenRelAndStagesValid() {
        // SPE-L3: entity-level when is always-active; no dispatch warn / peer hard error.
        var entity = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Idle", [], [], [], [])
        ]) {
            Subscriptions = [
                new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [])
            ]
        };
        var order = new Entity("Order", [], [], [], [
            new Stage("Active", [], [], [], [])
        ]);
        var domain = new Domain("Test", [entity, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch
            && d.Message.Contains("Entity-level", StringComparison.Ordinal))).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionEffectBinding
            && d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task EntityLevelSubscription_Fires_WhenSubscriberNotInStageWithWhen() {
        // SPE-L2: entity-level when fires regardless of subscriber stage (Idle has no stage-local when).
        var tracker = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Idle", [], [], [], []),
            new Stage("Busy", [], [], [], [])
        ]) {
            Subscriptions = [
                new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                    new AssignEffect(
                        DomainExpression.Property("Status"),
                        DomainExpression.Literal("NOTIFIED"))
                ])
            ]
        };

        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [
            new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var domain = new Domain("EntityLevelNotify", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);

        var store = new DomainInstanceStore();
        var orderInstance = DomainEntityInstance.Create(order,
            new Dictionary<string, object?> { ["Code"] = "X" }, domain: domain);
        var trackerInstance = DomainEntityInstance.Create(tracker,
            new Dictionary<string, object?> { ["Status"] = "IDLE" }, domain: domain);
        store.Add(orderInstance);
        store.Add(trackerInstance);
        store.Link("Tracks", trackerInstance, orderInstance);

        await Assert.That(trackerInstance.CurrentStage).IsEqualTo("Idle");
        orderInstance.InvokeAction("Activate");

        await Assert.That(trackerInstance.GetProperty<string>("Status")).IsEqualTo("NOTIFIED");
    }

    [Test]
    public async Task EntityLevelAndStageSubscription_StageFirstThenEntityLevel() {
        // SPE-L2 order lock: stage-scoped effects run before entity-level for the same notify.
        var tracker = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("Status"),
                            DomainExpression.Literal("STAGE"))
                    ])
                ]
            }
        ]) {
            Subscriptions = [
                new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                    new AssignEffect(
                        DomainExpression.Property("Status"),
                        DomainExpression.Literal("ENTITY"))
                ])
            ]
        };

        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [
            new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var domain = new Domain("DispatchOrder", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);

        var store = new DomainInstanceStore();
        var orderInstance = DomainEntityInstance.Create(order,
            new Dictionary<string, object?> { ["Code"] = "X" }, domain: domain);
        var trackerInstance = DomainEntityInstance.Create(tracker,
            new Dictionary<string, object?> { ["Status"] = "INIT" }, domain: domain);
        store.Add(orderInstance);
        store.Add(trackerInstance);
        store.Link("Tracks", trackerInstance, orderInstance);

        orderInstance.InvokeAction("Activate");

        // Last writer wins: entity-level after stage → ENTITY
        await Assert.That(trackerInstance.GetProperty<string>("Status")).IsEqualTo("ENTITY");
    }

    [Test]
    public async Task StageLocalSubscription_StillFires_AlongsideEntityPath() {
        // Sibling: stage-local when still works (entity bag empty for this entity).
        var tracker = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("Status"),
                            DomainExpression.Literal("STAGE_ONLY"))
                    ])
                ]
            }
        ]);

        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [
            new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var domain = new Domain("StageLocalSibling", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);

        var store = new DomainInstanceStore();
        var orderInstance = DomainEntityInstance.Create(order,
            new Dictionary<string, object?> { ["Code"] = "X" }, domain: domain);
        var trackerInstance = DomainEntityInstance.Create(tracker,
            new Dictionary<string, object?> { ["Status"] = "INIT" }, domain: domain);
        store.Add(orderInstance);
        store.Add(trackerInstance);
        store.Link("Tracks", trackerInstance, orderInstance);

        orderInstance.InvokeAction("Activate");

        await Assert.That(trackerInstance.GetProperty<string>("Status")).IsEqualTo("STAGE_ONLY");
    }

    [Test]
    public async Task EntityLevelSubscription_WithPeerBinding_AnalysisAccepts() {
        // SPE-L3: as name on entity-level is allowed under the same rules as stage.
        var entity = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Idle", [], [], [], [])
        ]) {
            Subscriptions = [
                new StageSubscription(
                    RelationshipName: "Tracks",
                    StageNames: ["Active"],
                    Quantifier: StageSubscriptionQuantifier.Each,
                    Effects: [
                        new AssignEffect(
                            DomainExpression.Property("Status"),
                            DomainExpression.RelationshipNav("order",
                                DomainExpression.Property("Code")))
                    ],
                    PeerBinding: "order")
            ]
        };
        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Active", [], [], [], [])
        ]);
        var domain = new Domain("Test", [entity, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch
            && d.Message.Contains("peer binder", StringComparison.Ordinal))).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionEffectBinding
            && d.Severity == DiagnosticSeverity.Error)).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("Entity-level", StringComparison.Ordinal)
            && d.Message.Contains("not supported", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task EntityLevelSubscription_PeerBinding_CopiesPeerProperty() {
        // SPE-L3 golden: entity-level when Tracks Active as order { assign Status to order Code }
        var tracker = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Idle", [], [], [], [])
        ]) {
            Subscriptions = [
                new StageSubscription(
                    RelationshipName: "Tracks",
                    StageNames: ["Active"],
                    Quantifier: StageSubscriptionQuantifier.Each,
                    Effects: [
                        new AssignEffect(
                            DomainExpression.Property("Status"),
                            DomainExpression.RelationshipNav("order",
                                DomainExpression.Property("Code")))
                    ],
                    PeerBinding: "order")
            ]
        };

        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [
            new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var domain = new Domain("EntityLevelPeer", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionEffectBinding
            && d.Severity == DiagnosticSeverity.Error)).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch
            && d.Message.Contains("peer binder", StringComparison.Ordinal))).IsFalse();

        var store = new DomainInstanceStore();
        var orderInstance = DomainEntityInstance.Create(order,
            new Dictionary<string, object?> { ["Code"] = "EL-PEER-99" }, domain: domain);
        var trackerInstance = DomainEntityInstance.Create(tracker,
            new Dictionary<string, object?> { ["Status"] = "UNTOUCHED" }, domain: domain);
        store.Add(orderInstance);
        store.Add(trackerInstance);
        store.Link("Tracks", trackerInstance, orderInstance);

        await Assert.That(trackerInstance.CurrentStage).IsEqualTo("Idle");
        orderInstance.InvokeAction("Activate");

        await Assert.That(trackerInstance.GetProperty<string>("Status")).IsEqualTo("EL-PEER-99");
    }

    [Test]
    public async Task EntityLevelSubscription_UnboundPathPrefix_AnalysisError() {
        // F11: entity-level runs same binding fail-closed as stage.
        var entity = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], []) {
            Subscriptions = [
                new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                    new AssignEffect(
                        DomainExpression.Property("Status"),
                        DomainExpression.RelationshipNav("order",
                            DomainExpression.Property("Code")))
                ])
            ]
        };
        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Active", [], [], [], [])
        ]);
        var domain = new Domain("Test", [entity, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionEffectBinding
            && d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("order", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task SubscriptionEffect_NestedPeerPath_AnalysisError() {
        var tracker = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription(
                        RelationshipName: "Tracks",
                        StageNames: ["Active"],
                        Quantifier: StageSubscriptionQuantifier.Each,
                        Effects: [
                            new AssignEffect(
                                DomainExpression.Property("Status"),
                                DomainExpression.RelationshipNav("order",
                                    DomainExpression.RelationshipNav("Item",
                                        DomainExpression.Property("Price"))))
                        ],
                        PeerBinding: "order")
                ]
            }
        ]);
        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);
        var domain = new Domain("Test", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionEffectBinding
            && d.Message.Contains("Nested path-prefix", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task SubscriptionEffect_PeerAsAssignTarget_AnalysisError() {
        var tracker = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription(
                        RelationshipName: "Tracks",
                        StageNames: ["Active"],
                        Quantifier: StageSubscriptionQuantifier.Each,
                        Effects: [
                            new AssignEffect(
                                DomainExpression.RelationshipNav("order",
                                    DomainExpression.Property("Code")),
                                DomainExpression.Literal("X"))
                        ],
                        PeerBinding: "order")
                ]
            }
        ]);
        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);
        var domain = new Domain("Test", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionEffectBinding
            && d.Message.Contains("assign target", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task SubscriptionEffect_AnyQuantifier_PeerBinding_CopiesPeerProperty() {
        // F14: Any/All store path passes PeerBinding (product DSL is Each-only).
        var tracker = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription(
                        RelationshipName: "Tracks",
                        StageNames: ["Active"],
                        Quantifier: StageSubscriptionQuantifier.Any,
                        Effects: [
                            new AssignEffect(
                                DomainExpression.Property("Status"),
                                DomainExpression.RelationshipNav("order",
                                    DomainExpression.Property("Code")))
                        ],
                        PeerBinding: "order")
                ]
            }
        ]);
        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [
            new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);
        var domain = new Domain("Test", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToMany, [])
        ]);

        var store = new DomainInstanceStore();
        var orderInstance = DomainEntityInstance.Create(order,
            new Dictionary<string, object?> { ["Code"] = "ANY-1" }, domain: domain);
        var trackerInstance = DomainEntityInstance.Create(tracker,
            new Dictionary<string, object?> { ["Status"] = "IDLE" }, domain: domain);
        store.Add(orderInstance);
        store.Add(trackerInstance);
        store.Link("Tracks", trackerInstance, orderInstance);

        orderInstance.InvokeAction("Activate");
        await Assert.That(trackerInstance.GetProperty<string>("Status")).IsEqualTo("ANY-1");
    }

    [Test]
    public async Task OnEntryEffect_RunsOnStageTransition() {
        // BR.3.1: OnEntry effects execute when entering a stage.
        var entryTarget = new Property("EntryTarget", new DomainTypeReference("Text"), []);
        var status = new Property("Status", new DomainTypeReference("Text"), []);
        var entity = new Entity("TestEnt", [status, entryTarget], [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [
                // OnEntryEffects
                new AssignEffect(
                    DomainExpression.Property("Status"),
                    DomainExpression.Literal("EnteredActive")),
                new AssignEffect(
                    DomainExpression.Property("EntryTarget"),
                    DomainExpression.Property("Status"))
            ], [])
        ]);

        var domain = new Domain("Test", [entity], []);
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Status"] = "Initial", ["EntryTarget"] = "" },
            domain: domain);

        instance.InvokeAction("Go");

        await Assert.That(instance.CurrentStage).IsEqualTo("Active");
        await Assert.That(instance.GetProperty<string>("Status")).IsEqualTo("EnteredActive");
        // OnEntry ran after the stage was set, so EntryTarget copies the already-assigned Status
        await Assert.That(instance.GetProperty<string>("EntryTarget")).IsEqualTo("EnteredActive");
    }

    [Test]
    public async Task OnExitEffect_RunsBeforeStageTransition() {
        // BR.3.1: OnExit effects execute before leaving a stage.
        var exitNote = new Property("ExitNote", new DomainTypeReference("Text"), []);
        var entity = new Entity("TestEnt", [exitNote], [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], [
                // OnExitEffects
                new AssignEffect(
                    DomainExpression.Property("ExitNote"),
                    DomainExpression.Literal("LeftDraft"))
            ]),
            new Stage("Active", [], [], [], [])
        ]);

        var domain = new Domain("Test", [entity], []);
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["ExitNote"] = "" },
            domain: domain);

        // No initial stage transition — starts in Draft (first stage)
        instance.InvokeAction("Go");

        await Assert.That(instance.CurrentStage).IsEqualTo("Active");
        await Assert.That(instance.GetProperty<string>("ExitNote")).IsEqualTo("LeftDraft");
    }

    [Test]
    public async Task StageScopedAction_OnlyCallableFromThatStage() {
        // BR.3.2: Actions defined on a stage are only available while on that stage.
        var count = new Property("Count", new DomainTypeReference("Number"), []);
        var stageAction = new Poly.DomainModeling.Action("StageOnly", InvocationResult.Void, [], [
            new AssignEffect(
                DomainExpression.Property("Count"),
                DomainExpression.Add(DomainExpression.Property("Count"), DomainExpression.Literal(1L)))
        ], []);
        var entity = new Entity("TestEnt", [count], [
            // Entity-level action to transition
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("StageA"))
            ], [])
        ], [], [
            new Stage("Draft", [stageAction], [], [], []),
            new Stage("StageA", [], [], [], [])
        ]);

        var domain = new Domain("Test", [entity], []);
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Count"] = 0L },
            domain: domain);

        // Starts in Draft, can call StageOnly
        var result1 = instance.InvokeAction("StageOnly");
        await Assert.That(result1.Succeeded).IsTrue();
        await Assert.That(instance.GetProperty<object>("Count")).IsEqualTo(1L);

        // Transition to StageA, which has no actions
        instance.InvokeAction("Go");
        await Assert.That(instance.CurrentStage).IsEqualTo("StageA");

        // StageOnly no longer available
        var result2 = instance.InvokeAction("StageOnly");
        await Assert.That(result2.Succeeded).IsFalse();
        await Assert.That(result2.ErrorMessage).IsNotNull();
    }

    [Test]
    public async Task CreateEntityInstance_AutoAddsToStore() {
        // BR.3.3: Children created via CreateEntityInstance are auto-added to the parent's store.
        var childName = new Property("ChildName", new DomainTypeReference("Text"), []);
        var parentName = new Property("ParentName", new DomainTypeReference("Text"), []);
        var child = new Entity("Child", [childName], [], [], []);
        var parent = new Entity("Parent", [parentName], [
            new Poly.DomainModeling.Action("Spawn", InvocationResult.Void, [], [
                new CreateEntityInstance(new DomainTypeReference("Child"),
                    Initializers: [new PropertyBinding("ChildName", DomainExpression.Literal("AutoAdded"))])
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var domain = new Domain("Test", [parent, child], []);
        var store = new DomainInstanceStore();
        var parentInstance = DomainEntityInstance.Create(parent,
            new Dictionary<string, object?> { ["ParentName"] = "Parent" },
            domain: domain);
        store.Add(parentInstance);

        parentInstance.InvokeAction("Spawn");

        await Assert.That(parentInstance.CreatedChildren.Count).IsEqualTo(1);
        var childInstance = parentInstance.CreatedChildren[0];
        await Assert.That(childInstance.GetProperty<string>("ChildName")).IsEqualTo("AutoAdded");

        // Child should be in the store (auto-added by CreateChildInstance)
        await Assert.That(childInstance.Store).IsNotNull();
    }

    // ── P2.1: Create → Link runtime ─────────────────────────────

    [Test]
    public async Task CreateEntityInstance_WithRelationship_LinksInStore() {
        // Create effect with RelationshipName set → child is linked in store.
        var childProp = new Property("ChildName", new DomainTypeReference("Text"), []);
        var child = new Entity("Child", [childProp], [], [], []);
        var parent = new Entity("Parent", [
            new Property("ParentName", new DomainTypeReference("Text"), [])
        ], [
            new Poly.DomainModeling.Action("Spawn", InvocationResult.Void, [], [
                new CreateEntityInstance(new DomainTypeReference("Child"),
                    [new PropertyBinding("ChildName", DomainExpression.Literal("Linked"))],
                    RelationshipName: "hasChild")
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var rel = new Relationship("hasChild",
            new DomainTypeReference("Parent"), new DomainTypeReference("Child"),
            RelationshipCardinality.OneToOne, []);
        var domain = new Domain("Test", [parent, child], [rel]);
        var store = new DomainInstanceStore();
        var parentInstance = DomainEntityInstance.Create(parent,
            new Dictionary<string, object?> { ["ParentName"] = "Parent" },
            domain: domain);
        store.Add(parentInstance);

        parentInstance.InvokeAction("Spawn");

        var childInstance = parentInstance.CreatedChildren[0];
        await Assert.That(store.IsLinked("hasChild", parentInstance, childInstance)).IsTrue();
    }

    [Test]
    public async Task CreateEntityInstance_WithoutRelationship_NotLinked() {
        // Create effect without RelationshipName → child is NOT linked.
        var child = new Entity("Child", [], [], [], []);
        var parent = new Entity("Parent", [], [
            new Poly.DomainModeling.Action("Spawn", InvocationResult.Void, [], [
                new CreateEntityInstance(new DomainTypeReference("Child"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var domain = new Domain("Test", [parent, child], []);
        var store = new DomainInstanceStore();
        var parentInstance = DomainEntityInstance.Create(parent, domain: domain);
        store.Add(parentInstance);

        parentInstance.InvokeAction("Spawn");

        var childInstance = parentInstance.CreatedChildren[0];
        await Assert.That(store.IsLinked("child", parentInstance, childInstance)).IsFalse();
    }

    // ── P2.3: Dogfood golden path (via DSL) ────────────────────

    [Test]
    public async Task Dogfood_CreateInDSL_SubscriptionFires() {
        // Full golden path via .poly parse → evolve → execute.
        // Customer has create-in action, Order has stage transition,
        // Customer subscribes to linked Order's stage change.
        var poly = """
            domain Test

            Customer: entity {
              Status: Text
              Pending: stage {
                PlaceOrder: action {
                  create in orders { Status: "New" }
                }
                when orders Active {
                  assign Status to "Fulfilled"
                }
              }
              orders: many Order
            }

            Order: entity {
              Status: Text
              Draft: stage {
                Activate: action {
                  transition to Active
                }
              }
              Active: stage {}
            }
            """;

        // Parse and evolve
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var domain = new Domain("_", [], []);
        var evolveResult = new DomainEvolution(domain).Apply(changes);
        await Assert.That(evolveResult.Succeeded).IsTrue();

        // Verify the domain has the relationship and create-in effect
        var customerEntity = evolveResult.Root.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var orderEntity = evolveResult.Root.Types.OfType<Entity>().Single(e => e.Name == "Order");
        await Assert.That(evolveResult.Root.Relationships.Count).IsEqualTo(1);
        await Assert.That(evolveResult.Root.Relationships[0].Name).IsEqualTo("orders");

        var placeOrder = customerEntity.Stages
            .SelectMany(s => s.Actions)
            .First(a => a.Name == "PlaceOrder");
        await Assert.That(placeOrder.Effects.Count).IsEqualTo(1);
        await Assert.That(placeOrder.Effects[0]).IsTypeOf<CreateEntityInRelationshipEffect>();

        // Execute the golden path
        var store = new DomainInstanceStore();
        var custInstance = DomainEntityInstance.Create(customerEntity,
            new Dictionary<string, object?> { ["Status"] = "Idle" },
            domain: evolveResult.Root);

        // Register instances
        store.Add(custInstance);

        // PlaceOrder → create Order + auto-link
        custInstance.InvokeAction("PlaceOrder");
        await Assert.That(custInstance.CreatedChildren.Count).IsEqualTo(1);
        var orderInstance = custInstance.CreatedChildren[0];
        await Assert.That(orderInstance.Entity.Name).IsEqualTo("Order");
        await Assert.That(store.IsLinked("orders", custInstance, orderInstance)).IsTrue();
        await Assert.That(orderInstance.GetProperty<string>("Status")).IsEqualTo("New");

        // Order transitions to Active → Customer's subscription fires
        orderInstance.InvokeAction("Activate");
        await Assert.That(orderInstance.CurrentStage).IsEqualTo("Active");
        await Assert.That(custInstance.GetProperty<string>("Status")).IsEqualTo("Fulfilled");

        // export_dsl should be honest
        var printer = new DomainDslPrinter();
        var printed = printer.Print(evolveResult.Root);
        await Assert.That(printed.Contains("create in orders")).IsTrue();
        await Assert.That(printed.Contains("create Order")).IsFalse(); // uses create in, not plain create

        // Re-parse printed output
        var parser2 = new PolyDslParser(printed);
        var changes2 = parser2.Parse();
        var domain2 = new Domain("_", [], []);
        var evolveResult2 = new DomainEvolution(domain2).Apply(changes2);
        await Assert.That(evolveResult2.Succeeded).IsTrue();
    }

    [Test]
    public async Task InvokeAction_CreateLinkedChild_SubscriptionFires() {
        // Golden P2.1 path: create a linked child, then transition it.
        // Customer ──places──► Order. Customer subscribes to Order's transition.
        var orderStatus = new Property("OrderStatus", new DomainTypeReference("Text"), []);
        var order = new Entity("Order", [orderStatus], [
            new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var custStatus = new Property("CustStatus", new DomainTypeReference("Text"), []);
        var customer = new Entity("Customer", [custStatus], [
            new Poly.DomainModeling.Action("PlaceOrder", InvocationResult.Void, [], [
                // Create + auto-link via "places" relationship
                new CreateEntityInstance(new DomainTypeReference("Order"),
                    [new PropertyBinding("OrderStatus", DomainExpression.Literal("New"))],
                    RelationshipName: "places")
            ], [])
        ], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("places", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("CustStatus"),
                            DomainExpression.Literal("Fulfilled"))
                    ])
                ]
            },
            new Stage("Done", [], [], [], [])
        ]);

        var rel = new Relationship("places",
            new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);

        var domain = new Domain("Test", [customer, order], [rel]);

        var store = new DomainInstanceStore();
        var custInstance = DomainEntityInstance.Create(customer,
            new Dictionary<string, object?> { ["CustStatus"] = "PendingWait" },
            domain: domain);
        store.Add(custInstance);

        // PlaceOrder creates child Order, auto-links it via "places"
        custInstance.InvokeAction("PlaceOrder");
        await Assert.That(custInstance.CreatedChildren.Count).IsEqualTo(1);
        var orderInstance = custInstance.CreatedChildren[0];
        await Assert.That(store.IsLinked("places", custInstance, orderInstance)).IsTrue();

        // Order starts in Draft, then transitions to Active.
        // Because it's linked via "places", Customer's subscription should fire.
        orderInstance.InvokeAction("Activate");
        await Assert.That(orderInstance.CurrentStage).IsEqualTo("Active");
        await Assert.That(custInstance.GetProperty<string>("CustStatus")).IsEqualTo("Fulfilled");
    }

    [Test]
    public async Task CreateEntityInstance_RelationshipNameWithoutStore_NoOp() {
        // Create with RelationshipName but no store → no crash, no link.
        var child = new Entity("Child", [], [], [], []);
        var parent = new Entity("Parent", [], [
            new Poly.DomainModeling.Action("Spawn", InvocationResult.Void, [], [
                new CreateEntityInstance(new DomainTypeReference("Child"), [],
                    RelationshipName: "someRel")
            ], [])
        ], [], []);

        var domain = new Domain("Test", [parent, child], []);
        var parentInstance = DomainEntityInstance.Create(parent, domain: domain);

        // No store → should not crash
        parentInstance.InvokeAction("Spawn");
        await Assert.That(parentInstance.CreatedChildren.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Subscription_OneHop_Cascade() {
        // BR.3.4: Subscription body transitions subscriber. A ──rel──► B.
        // When B goes to Active, A's subscription fires and transitions A to Done.
        // The store recurses but this is only one hop (actual depth-limit
        // enforcement is tested in <see cref="Subscription_Cascade_ExceedsDepthLimit"/>).
        var aStatus = new Property("Status", new DomainTypeReference("Text"), []);
        var a = new Entity("A", [aStatus], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("rel", ["Active"], StageSubscriptionQuantifier.Each, [
                        new StageTransitionEffect(new StageReference("Done"))
                    ])
                ]
            },
            new Stage("Done", [], [], [], [])
        ]);

        var b = new Entity("B", [], [
            new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var rel = new Relationship("rel",
            new DomainTypeReference("A"), new DomainTypeReference("B"),
            RelationshipCardinality.OneToOne, []);

        var domain = new Domain("Test", [a, b], [rel]);

        var store = new DomainInstanceStore();
        var bInstance = DomainEntityInstance.Create(b, domain: domain);
        var aInstance = DomainEntityInstance.Create(a, domain: domain);
        store.Add(bInstance);
        store.Add(aInstance);
        store.Link("rel", aInstance, bInstance);

        await Assert.That(aInstance.CurrentStage).IsEqualTo("Pending");

        bInstance.InvokeAction("Activate");

        await Assert.That(bInstance.CurrentStage).IsEqualTo("Active");
        // A's subscription fired and transitioned A to Done
        await Assert.That(aInstance.CurrentStage).IsEqualTo("Done");
        // No exception — depth limit was not exceeded
    }

    [Test]
    public async Task StageAction_NotInheritedFromOtherStage() {
        // Stage hierarchy not supported — actions are only accessible on their own stage.
        var count = new Property("Count", new DomainTypeReference("Number"), []);
        var childAction = new Poly.DomainModeling.Action("ChildOp", InvocationResult.Void, [], [
            new AssignEffect(
                DomainExpression.Property("Count"),
                DomainExpression.Add(DomainExpression.Property("Count"), DomainExpression.Literal(10L)))
        ], []);
        var parentAction = new Poly.DomainModeling.Action("ParentOp", InvocationResult.Void, [], [
            new AssignEffect(
                DomainExpression.Property("Count"),
                DomainExpression.Add(DomainExpression.Property("Count"), DomainExpression.Literal(1L)))
        ], []);

        var entity = new Entity("TestEnt", [count], [
            new Poly.DomainModeling.Action("GoToChild", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Child"))
            ], [])
        ], [], [
            new Stage("Root", [parentAction], [], [], []),
            new Stage("Child", [childAction], [], [], [])
        ]);

        var domain = new Domain("Test", [entity], []);
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Count"] = 0L },
            domain: domain);

        // Starts in Root — can call ParentOp directly
        var r1 = instance.InvokeAction("ParentOp");
        await Assert.That(r1.Succeeded).IsTrue();
        await Assert.That(instance.GetProperty<object>("Count")).IsEqualTo(1L);

        // ChildOp is on the Child stage — not accessible from Root
        var r2 = instance.InvokeAction("ChildOp");
        await Assert.That(r2.Succeeded).IsFalse();

        // Transition to Child stage
        instance.InvokeAction("GoToChild");
        await Assert.That(instance.CurrentStage).IsEqualTo("Child");

        // From Child stage, ParentOp is NOT inherited (flat stages)
        var r3 = instance.InvokeAction("ParentOp");
        await Assert.That(r3.Succeeded).IsFalse();

        // Can only call ChildOp directly on the Child stage
        var r4 = instance.InvokeAction("ChildOp");
        await Assert.That(r4.Succeeded).IsTrue();
        await Assert.That(instance.GetProperty<object>("Count")).IsEqualTo(11L);

        // Entity-level action GoToChild still works from any stage
        instance.InvokeAction("GoToChild"); // same stage, no-op effectively
    }

    [Test]
    public async Task OnEntryEffect_Throws_StageStillSet_NotifyStillFires() {
        // BR.3′′.2: OnEntry effect throws → stage is set, subscribers still notified.
        // B subscribes to A. When A enters Active, its OnEntry throws.
        // B's subscription should still fire (notify runs in finally).
        var aStatus = new Property("AStatus", new DomainTypeReference("Text"), []);
        // OnEntry effect that throws via a deliberate VM crash (bad property access).
        // The throw is on Active's OnEntryEffects, executes when A transitions Draft→Active.
        var a = new Entity("A", [aStatus], [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [
                // OnEntryEffects for Active — this one throws
                new AssignEffect(
                    DomainExpression.Property("Nonexistent"),
                    DomainExpression.Literal("boom"))
            ], [])
        ]);

        var bStatus = new Property("BStatus", new DomainTypeReference("Text"), []);
        var b = new Entity("B", [bStatus], [], [], [
            new Stage("Waiting", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("rel", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("BStatus"),
                            DomainExpression.Literal("Notified"))
                    ])
                ]
            },
            new Stage("Done", [], [], [], [])
        ]);

        var rel = new Relationship("rel",
            new DomainTypeReference("B"), new DomainTypeReference("A"),
            RelationshipCardinality.OneToOne, []);

        var domain = new Domain("Test", [a, b], [rel]);
        var store = new DomainInstanceStore();
        var aInstance = DomainEntityInstance.Create(a, domain: domain);
        var bInstance = DomainEntityInstance.Create(b, domain: domain);
        store.Add(aInstance);
        store.Add(bInstance);
        store.Link("rel", bInstance, aInstance);

        // Trigger transition — OnEntry throws, but notify should still fire in finally
        var threw = false;
        try {
            aInstance.InvokeAction("Go");
        }
        catch {
            // Expected — OnEntry effect throws
            threw = true;
        }
        await Assert.That(threw).IsTrue();

        // Stage is still set even though OnEntry threw
        await Assert.That(aInstance.CurrentStage).IsEqualTo("Active");

        // B was notified despite the OnEntry exception
        await Assert.That(bInstance.GetProperty<string>("BStatus")).IsEqualTo("Notified");
    }

    [Test]
    public async Task Subscription_Cascade_ExceedsDepthLimit() {
        // BR.3′.3: Prove maxDepth=10 enforcement. Chain: E0 → E1 → E2 → ... → E11
        // where each Ei subscribes to Ei-1 transitioning, then transitions itself.
        // Relationships go: Ei (Source) → Ei-1 (Target), so Ei is the subscriber.
        // E0 starts it via manual action; cascade propagates through recursive NotifyTransition.

        var status = new Property("Status", new DomainTypeReference("Text"), []);
        var allEntities = new List<Entity>();
        var relationships = new List<Relationship>();

        // E0: triggered by manual action — transitions to Active
        var e0 = new Entity("E0", [status], [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);
        allEntities.Add(e0);

        // E1..E11: each subscribes to the previous entity's transition
        for (int i = 1; i <= 11; i++) {
            var name = $"E{i}";
            var prevName = $"E{i - 1}";
            // Subscription: when prev enters Active → transition self to Active
            // The subscription is on this entity's stage; the relationship has
            // Source = this entity, Target = prev entity.
            var sub = new StageSubscription($"rel{i}", ["Active"], StageSubscriptionQuantifier.Each, [
                new StageTransitionEffect(new StageReference("Active"))
            ]);
            // E11 HAS a subscription (rel11, same pattern) — if notified,
            // it WOULD transition to Active. This proves the depth limit
            // (maxDepth=10) is what keeps E11 in Draft, not a missing subscription.
            var entity = new Entity(name, [status], [], [], [
                new Stage("Draft", [], [], [], []) {
                    Subscriptions = [sub]
                },
                new Stage("Active", [], [], [], [])
            ]);
            allEntities.Add(entity);
            // Relationship: Source=this(Ei), Target=prev(Ei-1)
            relationships.Add(new Relationship($"rel{i}",
                new DomainTypeReference(name), new DomainTypeReference(prevName),
                RelationshipCardinality.OneToOne, []));
        }

        var domain = new Domain("Test", allEntities, relationships);
        var store = new DomainInstanceStore();

        var instances = new List<DomainEntityInstance>();
        foreach (var e in allEntities) {
            var inst = DomainEntityInstance.Create(e, domain: domain);
            store.Add(inst);
            instances.Add(inst);
        }

        // Instance links: Ei (source/subscriber) → Ei-1 (target) for rel i
        for (int i = 1; i <= 11; i++)
            store.Link($"rel{i}", instances[i], instances[i - 1]);

        // Trigger the cascade
        instances[0].InvokeAction("Go");

        // E0 triggered manually — in Active
        await Assert.That(instances[0].CurrentStage).IsEqualTo("Active");
        // E1..E10 fired by cascade (10 hops)
        for (int i = 1; i <= 10; i++) {
            await Assert.That(instances[i].CurrentStage).IsEqualTo("Active");
        }

        // E11 should still be in Draft (depth limit stopped before it)
        await Assert.That(instances[11].CurrentStage).IsEqualTo("Draft");
    }

    [Test]
    public async Task CreateEntityInstance_ChildParticipatesInStore() {
        // BR.3′.4: Child auto-added to store participates in subscription fan-out.
        // Parent creates Child; when Child transitions to Active, the subscription
        // on the Grandparent should fire.
        var gpStatus = new Property("Status", new DomainTypeReference("Text"), []);
        var gp = new Entity("Grandparent", [gpStatus], [], [], [
            new Stage("Waiting", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("parentChild", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("Status"),
                            DomainExpression.Literal("ChildActivated"))
                    ])
                ]
            },
            new Stage("Done", [], [], [], [])
        ]);

        var childAction = new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
            new StageTransitionEffect(new StageReference("Active"))
        ], []);
        var child = new Entity("Child", [
            new Property("Name", new DomainTypeReference("Text"), [])
        ], [childAction], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var parent = new Entity("Parent", [
            new Property("PName", new DomainTypeReference("Text"), [])
        ], [
            new Poly.DomainModeling.Action("Spawn", InvocationResult.Void, [], [
                new CreateEntityInstance(new DomainTypeReference("Child"),
                    Initializers: [new PropertyBinding("Name", DomainExpression.Literal("AutoChild"))])
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var rel = new Relationship("parentChild",
            new DomainTypeReference("Grandparent"), new DomainTypeReference("Child"),
            RelationshipCardinality.OneToOne, []);

        var domain = new Domain("Test", [gp, parent, child], [rel]);

        var store = new DomainInstanceStore();
        var gpInstance = DomainEntityInstance.Create(gp, domain: domain);
        var parentInstance = DomainEntityInstance.Create(parent, domain: domain);
        store.Add(gpInstance);
        store.Add(parentInstance);

        // Parent spawns Child — child should be auto-added to store
        parentInstance.InvokeAction("Spawn");
        await Assert.That(parentInstance.CreatedChildren.Count).IsEqualTo(1);
        var childInstance = parentInstance.CreatedChildren[0];
        await Assert.That(childInstance.Store).IsNotNull();

        // Instance link required for subscription fan-out
        store.Link("parentChild", gpInstance, childInstance);

        // Child transitions to Active — Grandparent's subscription should fire
        childInstance.InvokeAction("Activate");
        await Assert.That(childInstance.CurrentStage).IsEqualTo("Active");
        await Assert.That(gpInstance.GetProperty<string>("Status")).IsEqualTo("ChildActivated");
    }

    [Test]
    public async Task InstanceLinks_TwoByTwo_OnlyLinkedSubscriberFires() {
        // BR.4.4 / IG golden: 2 Trackers × 2 Orders — only the linked Tracker reacts.
        var statusProp = new Property("Status", new DomainTypeReference("Text"), []);
        var tracker = new Entity("Tracker", [statusProp], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("Status"),
                            DomainExpression.Literal("Triggered"))
                    ])
                ]
            }
        ]);

        var order = new Entity("Order", [], [
            new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var rel = new Relationship("Tracks",
            new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToOne, []);

        var domain = new Domain("Test", [tracker, order], [rel]);
        var store = new DomainInstanceStore();

        var order1 = DomainEntityInstance.Create(order, domain: domain);
        var order2 = DomainEntityInstance.Create(order, domain: domain);
        var tracker1 = DomainEntityInstance.Create(tracker,
            new Dictionary<string, object?> { ["Status"] = "Idle1" }, domain: domain);
        var tracker2 = DomainEntityInstance.Create(tracker,
            new Dictionary<string, object?> { ["Status"] = "Idle2" }, domain: domain);

        store.Add(order1);
        store.Add(order2);
        store.Add(tracker1);
        store.Add(tracker2);

        // Only tracker1 watches order1
        store.Link("Tracks", tracker1, order1);

        order1.InvokeAction("Activate");

        await Assert.That(tracker1.GetProperty<string>("Status")).IsEqualTo("Triggered");
        await Assert.That(tracker2.GetProperty<string>("Status")).IsEqualTo("Idle2");

        // order2 has no links — neither tracker should fire
        order2.InvokeAction("Activate");
        await Assert.That(tracker1.GetProperty<string>("Status")).IsEqualTo("Triggered");
        await Assert.That(tracker2.GetProperty<string>("Status")).IsEqualTo("Idle2");
    }

    [Test]
    public async Task Store_Unlink_StopsSubscriptionFanOut() {
        var statusProp = new Property("Status", new DomainTypeReference("Text"), []);
        var tracker = new Entity("Tracker", [statusProp], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("Status"),
                            DomainExpression.Literal("Triggered"))
                    ])
                ]
            }
        ]);

        var order = new Entity("Order", [], [
            new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], []),
            new Poly.DomainModeling.Action("Reset", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Draft"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var rel = new Relationship("Tracks",
            new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToOne, []);

        var domain = new Domain("Test", [tracker, order], [rel]);
        var store = new DomainInstanceStore();
        var orderInstance = DomainEntityInstance.Create(order, domain: domain);
        var trackerInstance = DomainEntityInstance.Create(tracker,
            new Dictionary<string, object?> { ["Status"] = "Idle" }, domain: domain);
        store.Add(orderInstance);
        store.Add(trackerInstance);
        store.Link("Tracks", trackerInstance, orderInstance);

        orderInstance.InvokeAction("Activate");
        await Assert.That(trackerInstance.GetProperty<string>("Status")).IsEqualTo("Triggered");

        // Reset and unlink — second activation must not fire
        orderInstance.InvokeAction("Reset");
        trackerInstance.SetProperty("Status", "Idle");
        store.Unlink("Tracks", trackerInstance, orderInstance);

        orderInstance.InvokeAction("Activate");
        await Assert.That(trackerInstance.GetProperty<string>("Status")).IsEqualTo("Idle");
    }

    [Test]
    public async Task InvokeAction_LinkRelationshipEffect_LinksViaPropertyBag() {
        // LinkRelationshipEffect target is a PropertyAccess whose value is a DomainEntityInstance.
        var tracker = new Entity("Tracker", [
            new Property("OrderRef", new DomainTypeReference("Text"), [])
        ], [
            new Poly.DomainModeling.Action("Attach", InvocationResult.Void, [], [
                new LinkRelationshipEffect("Tracks", DomainExpression.Property("OrderRef"))
            ], [])
        ], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [
                        new AssignEffect(
                            DomainExpression.Property("OrderRef"),
                            DomainExpression.Literal("linked-ok"))
                    ])
                ]
            }
        ]);

        // OrderRef holds a DomainEntityInstance at runtime (property type Text is only schema)
        var order = new Entity("Order", [], [
            new Poly.DomainModeling.Action("Activate", InvocationResult.Void, [], [
                new StageTransitionEffect(new StageReference("Active"))
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);

        var rel = new Relationship("Tracks",
            new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToOne, []);

        var domain = new Domain("Test", [tracker, order], [rel]);
        var store = new DomainInstanceStore();
        var orderInstance = DomainEntityInstance.Create(order, domain: domain);
        var trackerInstance = DomainEntityInstance.Create(tracker, domain: domain);
        store.Add(orderInstance);
        store.Add(trackerInstance);

        // Seed property bag with instance reference, then Link via InvokeAction effect
        trackerInstance.SetProperty("OrderRef", orderInstance);
        var attach = trackerInstance.InvokeAction("Attach");
        await Assert.That(attach.Succeeded).IsTrue();
        await Assert.That(store.IsLinked("Tracks", trackerInstance, orderInstance)).IsTrue();

        orderInstance.InvokeAction("Activate");
        await Assert.That(trackerInstance.GetProperty<string>("OrderRef")).IsEqualTo("linked-ok");
    }

    // ── P2′: Honesty residuals ──────────────────────────────────

    [Test]
    public async Task CreateEntityInstance_UnknownRelationship_FailsLoud() {
        // P2′.3: Create with a relationship name that doesn't exist in the domain should throw.
        var child = new Entity("Child", [], [], [], []);
        var parent = new Entity("Parent", [], [
            new Poly.DomainModeling.Action("Spawn", InvocationResult.Void, [], [
                new CreateEntityInstance(new DomainTypeReference("Child"), [],
                    RelationshipName: "nonexistentRel")
            ], [])
        ], [], []);

        var domain = new Domain("Test", [parent, child], []);
        var store = new DomainInstanceStore();
        var parentInstance = DomainEntityInstance.Create(parent, domain: domain);
        store.Add(parentInstance);

        var threw = false;
        try {
            parentInstance.InvokeAction("Spawn");
        }
        catch (InvalidOperationException ex) {
            await Assert.That(ex.Message.Contains("nonexistentRel")).IsTrue();
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task CreateEntityInRelationship_WithoutStore_NoCrash() {
        // P2′.3: CreateIn without store → should not crash, child still created.
        // Use direct API to avoid analysis gate issues (the create-in needs domain context)
        var orderEntity = new Entity("Order", [new Property("Title", new DomainTypeReference("Text"), [])], [], [], []);
        var customerEntity = new Entity("Customer", [new Property("Name", new DomainTypeReference("Text"), [])], [
            new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [
                new CreateEntityInRelationshipEffect("orders", [])
            ], [])
        ], [], [
            new Stage("Draft", [], [], [], [])
        ]);

        var domain = new Domain("Test", [customerEntity, orderEntity], [
            new Relationship("orders",
                new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToMany, [])
        ]);

        var custInstance = DomainEntityInstance.Create(customerEntity, domain: domain);

        // No store — the create-in resolves rel→target→creates child with RelationshipName,
        // then CreateChildInstance tries Store?.Add(child) (null → skip),
        // then tries to link (Store is null → skip). No crash expected.
        var threw = false;
        try {
            custInstance.InvokeAction("Go");
        }
        catch {
            threw = true;
        }
        await Assert.That(threw).IsFalse();
        await Assert.That(custInstance.CreatedChildren.Count).IsEqualTo(1);
    }

    // ── P2′′′.3 / P2′′′.4: Runtime source/target checks ─────────────────

    [Test]
    public async Task CreateEntityInstance_WithRelationshipName_WrongSource_FailsLoud() {
        // P2′′′.3: CreateEntityInstance + RelationshipName on wrong source entity should throw.
        var order = new Entity("Order", [], [], [], []);
        var customer = new Entity("Customer", [], [], [], []);
        var action = new Poly.DomainModeling.Action("Spawn", InvocationResult.Void, [], [
            new CreateEntityInstance(new DomainTypeReference("Order"), [],
                RelationshipName: "rel")
        ], []);
        var maker = new Entity("Maker", [], [action], [], []);
        var rel = new Relationship("rel",
            new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [maker, customer, order], [rel]);
        var store = new DomainInstanceStore();
        var makerInstance = DomainEntityInstance.Create(maker, domain: domain);
        store.Add(makerInstance);

        var threw = false;
        try {
            makerInstance.InvokeAction("Spawn");
        }
        catch (InvalidOperationException ex) {
            await Assert.That(ex.Message.Contains("not the source")).IsTrue();
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task CreateEntityInstance_WithRelationshipName_TargetTypeMismatch_FailsLoud() {
        // P2′′′.3: CreateEntityInstance + RelationshipName where created type ≠ rel target should throw.
        var invoice = new Entity("Invoice", [], [], [], []);
        var order = new Entity("Order", [], [], [], []);
        var action = new Poly.DomainModeling.Action("Spawn", InvocationResult.Void, [], [
            new CreateEntityInstance(new DomainTypeReference("Invoice"), [],
                RelationshipName: "rel")
        ], []);
        var customer = new Entity("Customer", [], [action], [], []);
        var rel = new Relationship("rel",
            new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [customer, order, invoice], [rel]);
        var store = new DomainInstanceStore();
        var custInstance = DomainEntityInstance.Create(customer, domain: domain);
        store.Add(custInstance);

        var threw = false;
        try {
            custInstance.InvokeAction("Spawn");
        }
        catch (InvalidOperationException ex) {
            await Assert.That(ex.Message.Contains("targets")).IsTrue();
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task CreateEntityInRelationship_WrongSource_FailsLoud() {
        // P2′′′.4: CreateIn effect on wrong source entity should throw at runtime.
        var order = new Entity("Order", [], [], [], []);
        var customer = new Entity("Customer", [], [], [], []);
        var action = new Poly.DomainModeling.Action("Spawn", InvocationResult.Void, [], [
            new CreateEntityInRelationshipEffect("rel", [])
        ], []);
        var maker = new Entity("Maker", [], [action], [], []);
        var rel = new Relationship("rel",
            new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [maker, customer, order], [rel]);
        var store = new DomainInstanceStore();
        var makerInstance = DomainEntityInstance.Create(maker, domain: domain);
        store.Add(makerInstance);

        var threw = false;
        try {
            makerInstance.InvokeAction("Spawn");
        }
        catch (InvalidOperationException ex) {
            await Assert.That(ex.Message.Contains("not the source")).IsTrue();
            threw = true;
        }
        await Assert.That(threw).IsTrue();
    }

    // ═════════════════════════════════════════════════════════════
    // Q3′ — Collection quantifier runtime evaluation
    // ═════════════════════════════════════════════════════════════

    [Test]
    public async Task EvaluatePolicy_AnyQuantifier_ReturnsTrueWhenMatched() {
        var target = new Entity("Target", [
            new Property("Value", new DomainTypeReference("Number"), [])
        ], [], [], []);
        var source = new Entity("Source", [], [], [
            new Policy("HasBig", DomainExpression.Any("items",
                DomainExpression.GreaterThan(DomainExpression.Property("Value"), DomainExpression.Literal(10L))))
        ], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var store = new DomainInstanceStore();
        var src = DomainEntityInstance.Create(source, domain: domain);
        var t1 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Value"] = 5L }, domain: domain);
        var t2 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Value"] = 20L }, domain: domain);
        store.Add(src); store.Add(t1); store.Add(t2);
        store.Link("items", src, t1); store.Link("items", src, t2);
        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Source").Policies.First(p => p.Name == "HasBig");
        await Assert.That(src.EvaluatePolicy(policy)).IsTrue();
    }

    [Test]
    public async Task EvaluatePolicy_AnyQuantifier_ReturnsFalseWhenUnmatched() {
        var target = new Entity("Target", [
            new Property("Value", new DomainTypeReference("Number"), [])
        ], [], [], []);
        var source = new Entity("Source", [], [], [
            new Policy("HasBig", DomainExpression.Any("items",
                DomainExpression.GreaterThan(DomainExpression.Property("Value"), DomainExpression.Literal(10L))))
        ], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var store = new DomainInstanceStore();
        var src = DomainEntityInstance.Create(source, domain: domain);
        var t1 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Value"] = 1L }, domain: domain);
        var t2 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Value"] = 5L }, domain: domain);
        store.Add(src); store.Add(t1); store.Add(t2);
        store.Link("items", src, t1); store.Link("items", src, t2);
        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Source").Policies.First(p => p.Name == "HasBig");
        await Assert.That(src.EvaluatePolicy(policy)).IsFalse();
    }

    [Test]
    public async Task EvaluatePolicy_AllQuantifier_ReturnsTrueWhenAllMatch() {
        var target = new Entity("Target", [
            new Property("Active", new DomainTypeReference("Boolean"), [])
        ], [], [], []);
        var source = new Entity("Source", [], [], [
            new Policy("AllActive", DomainExpression.All("items",
                DomainExpression.Equal(DomainExpression.Property("Active"), DomainExpression.Literal(true))))
        ], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var store = new DomainInstanceStore();
        var src = DomainEntityInstance.Create(source, domain: domain);
        var t1 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Active"] = true }, domain: domain);
        var t2 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Active"] = true }, domain: domain);
        store.Add(src); store.Add(t1); store.Add(t2);
        store.Link("items", src, t1); store.Link("items", src, t2);
        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Source").Policies.First(p => p.Name == "AllActive");
        await Assert.That(src.EvaluatePolicy(policy)).IsTrue();
    }

    [Test]
    public async Task EvaluatePolicy_AllQuantifier_ReturnsFalseWhenOneFails() {
        var target = new Entity("Target", [
            new Property("Active", new DomainTypeReference("Boolean"), [])
        ], [], [], []);
        var source = new Entity("Source", [], [], [
            new Policy("AllActive", DomainExpression.All("items",
                DomainExpression.Equal(DomainExpression.Property("Active"), DomainExpression.Literal(true))))
        ], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var store = new DomainInstanceStore();
        var src = DomainEntityInstance.Create(source, domain: domain);
        var t1 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Active"] = true }, domain: domain);
        var t2 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Active"] = false }, domain: domain);
        store.Add(src); store.Add(t1); store.Add(t2);
        store.Link("items", src, t1); store.Link("items", src, t2);
        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Source").Policies.First(p => p.Name == "AllActive");
        await Assert.That(src.EvaluatePolicy(policy)).IsFalse();
    }

    [Test]
    public async Task EvaluatePolicy_AllQuantifier_EmptySet_ReturnsFalse() {
        var target = new Entity("Target", [
            new Property("Active", new DomainTypeReference("Boolean"), [])
        ], [], [], []);
        var source = new Entity("Source", [], [], [
            new Policy("AllActive", DomainExpression.All("items",
                DomainExpression.Equal(DomainExpression.Property("Active"), DomainExpression.Literal(true))))
        ], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var store = new DomainInstanceStore();
        var src = DomainEntityInstance.Create(source, domain: domain);
        store.Add(src);
        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Source").Policies.First(p => p.Name == "AllActive");
        await Assert.That(src.EvaluatePolicy(policy)).IsFalse();
    }

    [Test]
    public async Task EvaluatePolicy_NoneQuantifier_ReturnsTrueWhenNoneMatch() {
        var target = new Entity("Target", [
            new Property("Value", new DomainTypeReference("Number"), [])
        ], [], [], []);
        var source = new Entity("Source", [], [], [
            new Policy("NoBig", DomainExpression.None("items",
                DomainExpression.GreaterThan(DomainExpression.Property("Value"), DomainExpression.Literal(10L))))
        ], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var store = new DomainInstanceStore();
        var src = DomainEntityInstance.Create(source, domain: domain);
        var t1 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Value"] = 1L }, domain: domain);
        var t2 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Value"] = 5L }, domain: domain);
        store.Add(src); store.Add(t1); store.Add(t2);
        store.Link("items", src, t1); store.Link("items", src, t2);
        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Source").Policies.First(p => p.Name == "NoBig");
        await Assert.That(src.EvaluatePolicy(policy)).IsTrue();
    }

    [Test]
    public async Task EvaluatePolicy_NoneQuantifier_ReturnsFalseWhenMatched() {
        var target = new Entity("Target", [
            new Property("Value", new DomainTypeReference("Number"), [])
        ], [], [], []);
        var source = new Entity("Source", [], [], [
            new Policy("NoBig", DomainExpression.None("items",
                DomainExpression.GreaterThan(DomainExpression.Property("Value"), DomainExpression.Literal(10L))))
        ], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var store = new DomainInstanceStore();
        var src = DomainEntityInstance.Create(source, domain: domain);
        var t1 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Value"] = 1L }, domain: domain);
        var t2 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Value"] = 20L }, domain: domain);
        store.Add(src); store.Add(t1); store.Add(t2);
        store.Link("items", src, t1); store.Link("items", src, t2);
        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Source").Policies.First(p => p.Name == "NoBig");
        await Assert.That(src.EvaluatePolicy(policy)).IsFalse();
    }

    [Test]
    public async Task EvaluatePolicy_CountQuantifier_Bare_ReturnsCount() {
        var target = new Entity("Target", [], [], [], []);
        var source = new Entity("Source", [], [], [
            new Policy("HasThree", DomainExpression.Equal(
                DomainExpression.Count("items", null), DomainExpression.Literal(3L)))
        ], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var store = new DomainInstanceStore();
        var src = DomainEntityInstance.Create(source, domain: domain);
        var t1 = DomainEntityInstance.Create(target, domain: domain);
        var t2 = DomainEntityInstance.Create(target, domain: domain);
        var t3 = DomainEntityInstance.Create(target, domain: domain);
        store.Add(src); store.Add(t1); store.Add(t2); store.Add(t3);
        store.Link("items", src, t1); store.Link("items", src, t2); store.Link("items", src, t3);
        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Source").Policies.First(p => p.Name == "HasThree");
        await Assert.That(src.EvaluatePolicy(policy)).IsTrue();
    }

    [Test]
    public async Task EvaluatePolicy_CountQuantifier_WithBody_ReturnsFilteredCount() {
        var target = new Entity("Target", [
            new Property("Value", new DomainTypeReference("Number"), [])
        ], [], [], []);
        var source = new Entity("Source", [], [], [
            new Policy("CountBig", DomainExpression.Equal(
                DomainExpression.Count("items", DomainExpression.GreaterThan(DomainExpression.Property("Value"), DomainExpression.Literal(10L))),
                DomainExpression.Literal(2L)))
        ], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var store = new DomainInstanceStore();
        var src = DomainEntityInstance.Create(source, domain: domain);
        var t1 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Value"] = 5L }, domain: domain);
        var t2 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Value"] = 20L }, domain: domain);
        var t3 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Value"] = 30L }, domain: domain);
        store.Add(src); store.Add(t1); store.Add(t2); store.Add(t3);
        store.Link("items", src, t1); store.Link("items", src, t2); store.Link("items", src, t3);
        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Source").Policies.First(p => p.Name == "CountBig");
        await Assert.That(src.EvaluatePolicy(policy)).IsTrue();
    }

    [Test]
    public async Task EvaluatePolicy_Quantifier_CombinedWithLocalProperty() {
        var target = new Entity("Target", [
            new Property("Value", new DomainTypeReference("Number"), [])
        ], [], [], []);
        var source = new Entity("Source", [
            new Property("Threshold", new DomainTypeReference("Number"), [])
        ], [], [
            new Policy("HasBig", DomainExpression.And(
                DomainExpression.GreaterThan(DomainExpression.Property("Threshold"), DomainExpression.Literal(0L)),
                DomainExpression.Any("items", DomainExpression.GreaterThan(DomainExpression.Property("Value"), DomainExpression.Literal(10L)))))
        ], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var store = new DomainInstanceStore();
        var src = DomainEntityInstance.Create(source, new Dictionary<string, object?> { ["Threshold"] = 5L }, domain: domain);
        var t1 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Value"] = 1L }, domain: domain);
        var t2 = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["Value"] = 20L }, domain: domain);
        store.Add(src); store.Add(t1); store.Add(t2);
        store.Link("items", src, t1); store.Link("items", src, t2);
        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Source").Policies.First(p => p.Name == "HasBig");
        await Assert.That(src.EvaluatePolicy(policy)).IsTrue();

        var src2 = DomainEntityInstance.Create(source, new Dictionary<string, object?> { ["Threshold"] = 0L }, domain: domain);
        store.Add(src2);
        await Assert.That(src2.EvaluatePolicy(policy)).IsFalse();
    }

    [Test]
    public async Task EvaluatePolicy_Quantifier_WithoutStore_Throws() {
        var target = new Entity("Target", [
            new Property("Value", new DomainTypeReference("Number"), [])
        ], [], [], []);
        var source = new Entity("Source", [], [], [
            new Policy("HasBig", DomainExpression.Any("items",
                DomainExpression.GreaterThan(DomainExpression.Property("Value"), DomainExpression.Literal(10L))))
        ], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var src = DomainEntityInstance.Create(source, domain: domain); // no store
        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Source").Policies.First(p => p.Name == "HasBig");
        await Assert.That(() => src.EvaluatePolicy(policy)).Throws<InvalidOperationException>();
    }

    // ── owned-3: to-one RelationshipNavigation in policy evaluation ──

    [Test]
    public async Task EvaluatePolicy_ToOneRelationshipNav_ResolvesLinkedProperty() {
        var target = new Entity("Profile", [
            new Property("City", new DomainTypeReference("Text"), [])
        ], [], [], []);
        var source = new Entity("Customer", [
            new Property("Name", new DomainTypeReference("Text"), [])
        ], [], [
            new Policy("IsUrban", DomainExpression.RelationshipNav("profile",
                DomainExpression.Equal(
                    DomainExpression.Property("City"),
                    DomainExpression.Literal("Metropolis"))))
        ], []);
        var rel = new Relationship("profile",
            new DomainTypeReference("Customer"), new DomainTypeReference("Profile"),
            RelationshipCardinality.OneToOne, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var store = new DomainInstanceStore();
        var cust = DomainEntityInstance.Create(source, new Dictionary<string, object?> { ["Name"] = "Alice" }, domain: domain);
        var profile = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["City"] = "Metropolis" }, domain: domain);
        store.Add(cust); store.Add(profile);
        store.Link("profile", cust, profile);

        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Customer").Policies.First(p => p.Name == "IsUrban");
        await Assert.That(cust.EvaluatePolicy(policy)).IsTrue();
    }

    [Test]
    public async Task EvaluatePolicy_ToOneRelationshipNav_NonMatching_ReturnsFalse() {
        var target = new Entity("Profile", [
            new Property("City", new DomainTypeReference("Text"), [])
        ], [], [], []);
        var source = new Entity("Customer", [
            new Property("Name", new DomainTypeReference("Text"), [])
        ], [], [
            new Policy("IsUrban", DomainExpression.RelationshipNav("profile",
                DomainExpression.Equal(
                    DomainExpression.Property("City"),
                    DomainExpression.Literal("Metropolis"))))
        ], []);
        var rel = new Relationship("profile",
            new DomainTypeReference("Customer"), new DomainTypeReference("Profile"),
            RelationshipCardinality.OneToOne, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var store = new DomainInstanceStore();
        var cust = DomainEntityInstance.Create(source, new Dictionary<string, object?> { ["Name"] = "Bob" }, domain: domain);
        var profile = DomainEntityInstance.Create(target, new Dictionary<string, object?> { ["City"] = "Gotham" }, domain: domain);
        store.Add(cust); store.Add(profile);
        store.Link("profile", cust, profile);

        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Customer").Policies.First(p => p.Name == "IsUrban");
        await Assert.That(cust.EvaluatePolicy(policy)).IsFalse();
    }

    [Test]
    public async Task EvaluatePolicy_ToOneRelationshipNav_WithoutStore_Throws() {
        var target = new Entity("Profile", [
            new Property("City", new DomainTypeReference("Text"), [])
        ], [], [], []);
        var source = new Entity("Customer", [
            new Property("Name", new DomainTypeReference("Text"), [])
        ], [], [
            new Policy("IsUrban", DomainExpression.RelationshipNav("profile",
                DomainExpression.Equal(
                    DomainExpression.Property("City"),
                    DomainExpression.Literal("Metropolis"))))
        ], []);
        var rel = new Relationship("profile",
            new DomainTypeReference("Customer"), new DomainTypeReference("Profile"),
            RelationshipCardinality.OneToOne, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var cust = DomainEntityInstance.Create(source,
            new Dictionary<string, object?> { ["Name"] = "Alice" }, domain: domain);
        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Customer").Policies.First(p => p.Name == "IsUrban");
        await Assert.That(() => cust.EvaluatePolicy(policy)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task EvaluatePolicy_ToOneRelationshipNav_Unlinked_Throws() {
        var target = new Entity("Profile", [
            new Property("City", new DomainTypeReference("Text"), [])
        ], [], [], []);
        var source = new Entity("Customer", [
            new Property("Name", new DomainTypeReference("Text"), [])
        ], [], [
            new Policy("IsUrban", DomainExpression.RelationshipNav("profile",
                DomainExpression.Equal(
                    DomainExpression.Property("City"),
                    DomainExpression.Literal("Metropolis"))))
        ], []);
        var rel = new Relationship("profile",
            new DomainTypeReference("Customer"), new DomainTypeReference("Profile"),
            RelationshipCardinality.OneToOne, []);
        var domain = new Domain("Test", [source, target], [rel]);
        var store = new DomainInstanceStore();
        var cust = DomainEntityInstance.Create(source,
            new Dictionary<string, object?> { ["Name"] = "Alice" }, domain: domain);
        store.Add(cust);

        var policy = domain.Types.OfType<Entity>().First(e => e.Name == "Customer").Policies.First(p => p.Name == "IsUrban");
        await Assert.That(() => cust.EvaluatePolicy(policy)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task EvaluatePolicy_PathPrefix_MultipleLinkedTargets_Throws() {
        // Pre-ship: bare path-prefix must not silently pick targets[0] on many-links.
        var item = new Entity("Item", [
            new Property("Sku", new DomainTypeReference("Text"), [])
        ], [], [], []);
        var order = new Entity("Order", [
            new Property("Name", new DomainTypeReference("Text"), [])
        ], [], [
            new Policy("HasSkuX", DomainExpression.RelationshipNav("items",
                DomainExpression.Equal(
                    DomainExpression.Property("Sku"),
                    DomainExpression.Literal("X"))))
        ], []);
        var rel = new Relationship("items",
            new DomainTypeReference("Order"), new DomainTypeReference("Item"),
            RelationshipCardinality.OneToMany, []);
        var domain = new Domain("Test", [order, item], [rel]);
        var store = new DomainInstanceStore();
        var orderInst = DomainEntityInstance.Create(order,
            new Dictionary<string, object?> { ["Name"] = "O1" }, domain: domain);
        var a = DomainEntityInstance.Create(item,
            new Dictionary<string, object?> { ["Sku"] = "X" }, domain: domain);
        var b = DomainEntityInstance.Create(item,
            new Dictionary<string, object?> { ["Sku"] = "Y" }, domain: domain);
        store.Add(orderInst);
        store.Add(a);
        store.Add(b);
        store.Link("items", orderInst, a);
        store.Link("items", orderInst, b);

        var policy = order.Policies.First(p => p.Name == "HasSkuX");
        var ex = Assert.Throws<InvalidOperationException>(() => orderInst.EvaluatePolicy(policy));
        await Assert.That(ex!.Message).Contains("exactly one linked target");
        await Assert.That(ex.Message).Contains("quantifiers");
    }
}