using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Effects;

namespace Poly.Tests.DomainModeling.Analysis;

public class DomainInstanceStoreFailClosedTests {
    private static Domain BuildDomainWithSubscriptions() {
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

        return new Domain("FailClosedTest", [order, tracker], [relationship]);
    }

    [Test]
    public async Task NotifyTransition_Throws_WhenRelationshipContractMetadataMissing() {
        // Arrange: Build a domain with subscriptions and create instances
        var domain = BuildDomainWithSubscriptions();
        var store = new DomainInstanceStore();

        var order = DomainEntityInstance.Create(
            (Entity)domain.Types[0], new Dictionary<string, object?>(), domain);
        var tracker = DomainEntityInstance.Create(
            (Entity)domain.Types[1], new Dictionary<string, object?>(), domain);

        store.Add(order);
        store.Add(tracker);
        store.Link("Tracks", tracker, order);

        // Corrupt the cache: remove RelationshipContractMetadata after analysis
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        analysis.GetMetadataStore().Remove<RelationshipContractMetadata>(null);

        // Act & Assert: throws when runtime metadata is missing
        await Assert.That(() => order.TransitionStage("Active"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task NotifyTransition_Throws_WhenEntityStructureMetadataMissing_ForSubscriber() {
        // Arrange
        var domain = BuildDomainWithSubscriptions();
        var store = new DomainInstanceStore();

        var order = DomainEntityInstance.Create(
            (Entity)domain.Types[0], new Dictionary<string, object?>(), domain);
        var tracker = DomainEntityInstance.Create(
            (Entity)domain.Types[1], new Dictionary<string, object?>(), domain);

        store.Add(order);
        store.Add(tracker);
        store.Link("Tracks", tracker, order);

        // Corrupt the cache: remove EntityStructureMetadata for the subscriber entity
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        analysis.GetMetadataStore().Remove<EntityStructureMetadata>((Entity)domain.Types[1]);

        // Act & Assert: throws when subscriber metadata is missing
        await Assert.That(() => order.TransitionStage("Active"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TransitionStage_Throws_WhenDomainCatalogMissing() {
        var domain = BuildDomainWithSubscriptions();
        var order = DomainEntityInstance.Create(
            (Entity)domain.Types[0], new Dictionary<string, object?>(), domain);

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        analysis.GetMetadataStore().Remove<DomainCatalogMetadata>(domain);

        var ex = Assert.Throws<InvalidOperationException>(() => order.TransitionStage("Active"));
        await Assert.That(ex!.Message).Contains("DomainCatalogMetadata");
        await Assert.That(ex.Message).Contains("TransitionStage");
    }

    [Test]
    public async Task NotifyTransition_Throws_WhenDomainCatalogMissing() {
        // Call store.NotifyTransition directly — TransitionStage would throw first (Q1).
        var domain = BuildDomainWithSubscriptions();
        var store = new DomainInstanceStore();

        var order = DomainEntityInstance.Create(
            (Entity)domain.Types[0], new Dictionary<string, object?>(), domain);
        var tracker = DomainEntityInstance.Create(
            (Entity)domain.Types[1], new Dictionary<string, object?>(), domain);

        store.Add(order);
        store.Add(tracker);
        store.Link("Tracks", tracker, order);

        // Advance stage without notify so we can hit NotifyTransition alone.
        order.TransitionStage("Active", notifyStore: false);

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        analysis.GetMetadataStore().Remove<DomainCatalogMetadata>(domain);

        var ex = Assert.Throws<InvalidOperationException>(() => store.NotifyTransition(order, "Active"));
        await Assert.That(ex!.Message).Contains("DomainCatalogMetadata");
        await Assert.That(ex.Message).Contains("NotifyTransition");
    }

    [Test]
    public async Task TransitionStage_DomainBound_Throws_WhenEntityStructureMetadataMissing() {
        var domain = BuildDomainWithSubscriptions();
        var orderEntity = (Entity)domain.Types[0];
        var order = DomainEntityInstance.Create(orderEntity, new Dictionary<string, object?>(), domain);

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        analysis.GetMetadataStore().Remove<EntityStructureMetadata>(orderEntity);

        var ex = Assert.Throws<InvalidOperationException>(() => order.TransitionStage("Active"));
        await Assert.That(ex!.Message).Contains("EntityStructureMetadata");
    }

    [Test]
    public async Task NotifyTransition_Succeeds_WhenAllMetadataPresent() {
        // Arrange: Full analysis without corruption — happy path
        var domain = BuildDomainWithSubscriptions();
        var store = new DomainInstanceStore();

        var order = DomainEntityInstance.Create(
            (Entity)domain.Types[0], new Dictionary<string, object?>(), domain);
        var tracker = DomainEntityInstance.Create(
            (Entity)domain.Types[1], new Dictionary<string, object?>(), domain);

        store.Add(order);
        store.Add(tracker);
        store.Link("Tracks", tracker, order);

        // Act & Assert: With all metadata present, no throw
        await Assert.That(() => order.TransitionStage("Active")).ThrowsNothing();
    }

    [Test]
    public async Task RuntimeAnalysisCache_ReturnedAnalysis_ContainsRequiredRuntimeMetadata() {
        // Arrange
        var domain = BuildDomainWithSubscriptions();

        // Act
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var tracker = (Entity)domain.Types[1];
        var pending = tracker.Stages[0];

        // Catalog owns action maps (DAS W1.4); stage plans + contracts remain separate.
        var catalog = analysis.GetMetadata<DomainCatalogMetadata>(domain);
        await Assert.That(catalog).IsNotNull();
        await Assert.That(catalog!.ActionsByEntityName.TryGetValue("Tracker", out var arm)).IsTrue();
        await Assert.That(arm!.EntityActions.ContainsKey("Reset")).IsTrue();

        var esm = analysis.GetMetadata<EntityStructureMetadata>(tracker);
        await Assert.That(esm).IsNotNull();
        await Assert.That(esm!.StageByName!.ContainsKey("Pending")).IsTrue();

        var rcm = analysis.GetMetadata<RelationshipContractMetadata>(default);
        await Assert.That(rcm).IsNotNull();
        await Assert.That(rcm!.Contracts.Count).IsGreaterThan(0);

        var sdm = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(pending);
        await Assert.That(sdm).IsNotNull();
        await Assert.That(sdm!.ByRelationshipName.ContainsKey("Tracks")).IsTrue();
    }

    [Test]
    public async Task NotifyTransition_NoThrow_WhenDomainIsNull() {
        // Arrange: Create an instance without a Domain reference so no analysis
        // is available. Use two stages so the transition reaches NotifyTransition.
        var initial = new Stage("Initial", [], [], [], []);
        var active = new Stage("Active", [], [], [], []);
        var entity = new Entity("Standalone",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            [], [],
            Stages: [initial, active]);
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?>(), domain: null);

        var store = new DomainInstanceStore();
        store.Add(instance);

        // Act & Assert: TransitionStage should not throw for a standalone instance
        // because the store's NotifyTransition returns early when Domain is null.
        await Assert.That(() => instance.TransitionStage("Active")).ThrowsNothing();
    }
}