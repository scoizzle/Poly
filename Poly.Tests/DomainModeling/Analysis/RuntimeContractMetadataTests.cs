using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;

namespace Poly.Tests.DomainModeling.Analysis;

public class RuntimeContractMetadataTests {
    private static Domain BuildDomain() {
        var submit = new Poly.DomainModeling.Action("Submit", InvocationResult.Void, [], [], []);
        var escalate = new Poly.DomainModeling.Action("Escalate", InvocationResult.Void, [], [], []);

        var orderPending = new Stage("Pending", [submit], [], [], []);
        var orderActive = new Stage("Active", [], [], [], []);
        var order = new Entity("Order",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [],
            Policies: [],
            Stages: [orderPending, orderActive]);

        var trackerPending = new Stage("Pending", [escalate], [], [], []) {
            Subscriptions = [
                new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [])
            ]
        };

        var tracker = new Entity("Tracker",
            [new Property("Label", new DomainTypeReference("Text"), [])],
            Actions: [new Poly.DomainModeling.Action("Reset", InvocationResult.Void, [], [], [])],
            Policies: [],
            Stages: [trackerPending]);

        var relationship = new Relationship(
            "Tracks",
            new DomainTypeReference("Tracker"),
            new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany,
            []);

        return new Domain("RuntimeContracts", [order, tracker], [relationship]);
    }

    [Test]
    public async Task Analyze_ProducesCatalogActionResolution_ForEntity() {
        var domain = BuildDomain();
        var tracker = domain.Types.OfType<Entity>().First(e => e.Name == "Tracker");

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var catalog = analysis.GetMetadata<DomainCatalogMetadata>(domain);

        await Assert.That(catalog).IsNotNull();
        await Assert.That(catalog!.ActionsByEntityName.TryGetValue("Tracker", out var metadata)).IsTrue();
        await Assert.That(metadata!.EntityActions.ContainsKey("Reset")).IsTrue();
        await Assert.That(metadata.StageActions.ContainsKey("Pending")).IsTrue();
        await Assert.That(metadata.StageActions["Pending"].ContainsKey("Escalate")).IsTrue();
        // No entity-keyed ARM dual-write (DAS W1.4).
        await Assert.That(analysis.GetMetadata<ActionResolutionMetadata>(tracker)).IsNull();
    }

    [Test]
    public async Task Analyze_ProducesRelationshipContractMetadata_ForDomain() {
        var domain = BuildDomain();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var metadata = analysis.GetMetadata<RelationshipContractMetadata>(default);

        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.Contracts.Count).IsEqualTo(1);

        var contract = metadata.Contracts[0];
        await Assert.That(contract.Name).IsEqualTo("Tracks");
        await Assert.That(contract.SourceEntityName).IsEqualTo("Tracker");
        await Assert.That(contract.TargetEntityName).IsEqualTo("Order");
        await Assert.That(contract.Cardinality).IsEqualTo(RelationshipCardinality.OneToMany);
    }

    [Test]
    public async Task Analyze_ProducesSubscriptionDispatchPlanMetadata_ForStage() {
        var domain = BuildDomain();
        var tracker = domain.Types.OfType<Entity>().First(e => e.Name == "Tracker");
        var pending = tracker.Stages.First(s => s.Name == "Pending");

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var metadata = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(pending);

        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.ByRelationshipName.ContainsKey("Tracks")).IsTrue();
        await Assert.That(metadata.ByRelationshipName["Tracks"].Count).IsEqualTo(1);

        var entry = metadata.ByRelationshipName["Tracks"][0];
        await Assert.That(entry.SourceEntityName).IsEqualTo("Tracker");
        await Assert.That(entry.TargetEntityName).IsEqualTo("Order");
        await Assert.That(entry.Quantifier).IsEqualTo(StageSubscriptionQuantifier.Each);
        await Assert.That(entry.StageNames.Contains("Active")).IsTrue();
    }

    [Test]
    public async Task Analyze_ProducesSubscriptionDispatchPlanMetadata_ForEntityLevelWhen() {
        var submit = new Poly.DomainModeling.Action("Submit", InvocationResult.Void, [], [], []);
        var orderPending = new Stage("Pending", [submit], [], [], []);
        var orderActive = new Stage("Active", [], [], [], []);
        var order = new Entity("Order",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [],
            Policies: [],
            Stages: [orderPending, orderActive]);

        var tracker = new Entity("Tracker",
            [new Property("Label", new DomainTypeReference("Text"), [])],
            Actions: [],
            Policies: [],
            Stages: [new Stage("Idle", [], [], [], [])]) {
            Subscriptions = [
                new StageSubscription(
                    RelationshipName: "Tracks",
                    StageNames: ["Active"],
                    Quantifier: StageSubscriptionQuantifier.Each,
                    Effects: [],
                    PeerBinding: null)
            ]
        };

        var relationship = new Relationship(
            "Tracks",
            new DomainTypeReference("Tracker"),
            new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany,
            []);

        var domain = new Domain("EntityLevelDispatch", [order, tracker], [relationship]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var entityPlan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(tracker);
        await Assert.That(entityPlan).IsNotNull();
        await Assert.That(entityPlan!.ByRelationshipName.ContainsKey("Tracks")).IsTrue();
        await Assert.That(entityPlan.ByRelationshipName["Tracks"].Count).IsEqualTo(1);

        var entry = entityPlan.ByRelationshipName["Tracks"][0];
        await Assert.That(entry.SourceEntityName).IsEqualTo("Tracker");
        await Assert.That(entry.TargetEntityName).IsEqualTo("Order");
        await Assert.That(entry.Quantifier).IsEqualTo(StageSubscriptionQuantifier.Each);
        await Assert.That(entry.StageNames.Contains("Active")).IsTrue();
        await Assert.That(entry.PeerBinding).IsNull();

        // Stage-only bags stay independent: Idle has no stage subs → empty plan.
        var idle = tracker.Stages.First(s => s.Name == "Idle");
        var stagePlan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(idle);
        await Assert.That(stagePlan).IsNotNull();
        await Assert.That(stagePlan!.ByRelationshipName.Count).IsEqualTo(0);

        // Order has no entity-level subs → empty entity plan; stage plans unchanged for stage-only.
        var orderEntityPlan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(order);
        await Assert.That(orderEntityPlan).IsNotNull();
        await Assert.That(orderEntityPlan!.ByRelationshipName.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Analyze_EntityLevelWhen_AmbiguousRelationship_ReportsStructuralFailure() {
        var tracker = new Entity("Tracker",
            [new Property("Label", new DomainTypeReference("Text"), [])],
            Actions: [],
            Policies: [],
            Stages: []) {
            Subscriptions = [
                new StageSubscription("Tracks", ["Active"], StageSubscriptionQuantifier.Each, [])
            ]
        };
        var order = new Entity("Order", [], [], [], [
            new Stage("Active", [], [], [], [])
        ]);
        // Two contracts same name+source is impossible via domain graph; zero match fails unique resolve.
        var domain = new Domain("AmbiguousEntitySub", [tracker, order], [
            new Relationship(
                "Other",
                new DomainTypeReference("Tracker"),
                new DomainTypeReference("Order"),
                RelationshipCardinality.OneToMany,
                [])
        ]);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SemanticReferenceResolution
            && d.Message.Contains("could not be uniquely resolved", StringComparison.Ordinal)
            && d.Message.Contains("Tracks", StringComparison.Ordinal))).IsTrue();

        var entityPlan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(tracker);
        await Assert.That(entityPlan).IsNotNull();
        await Assert.That(entityPlan!.ByRelationshipName.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Analyze_ProducesCatalogMutationIndex_ForDomain() {
        var domain = BuildDomain();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var catalog = analysis.GetMetadata<DomainCatalogMetadata>(domain);
        var index = catalog?.Index;

        await Assert.That(index).IsNotNull();
        await Assert.That(index!.TypesByName.ContainsKey("Order")).IsTrue();
        await Assert.That(index.EntitiesByName.ContainsKey("Tracker")).IsTrue();
        await Assert.That(index.RelationshipsByName.ContainsKey("Tracks")).IsTrue();
        await Assert.That(index.StagesByEntity["Tracker"].ContainsKey("Pending")).IsTrue();
        await Assert.That(index.ActionsByEntity["Tracker"].ContainsKey("Reset")).IsTrue();
        // No domain-keyed MTI dual-write (DAS W1.4).
        await Assert.That(analysis.GetMetadata<MutationTargetIndexMetadata>(domain)).IsNull();
    }
}