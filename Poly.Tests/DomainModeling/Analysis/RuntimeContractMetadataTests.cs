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
    public async Task Analyze_ProducesActionResolutionMetadata_ForEntity() {
        var domain = BuildDomain();
        var tracker = domain.Types.OfType<Entity>().First(e => e.Name == "Tracker");

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var metadata = analysis.GetMetadata<ActionResolutionMetadata>(tracker);

        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.EntityActions.ContainsKey("Reset")).IsTrue();
        await Assert.That(metadata.StageActions.ContainsKey("Pending")).IsTrue();
        await Assert.That(metadata.StageActions["Pending"].ContainsKey("Escalate")).IsTrue();
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
    public async Task Analyze_ProducesMutationTargetIndexMetadata_ForDomain() {
        var domain = BuildDomain();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var index = analysis.GetMetadata<MutationTargetIndexMetadata>(domain);

        await Assert.That(index).IsNotNull();
        await Assert.That(index!.TypesByName.ContainsKey("Order")).IsTrue();
        await Assert.That(index.EntitiesByName.ContainsKey("Tracker")).IsTrue();
        await Assert.That(index.RelationshipsByName.ContainsKey("Tracks")).IsTrue();
        await Assert.That(index.StagesByEntity["Tracker"].ContainsKey("Pending")).IsTrue();
        await Assert.That(index.ActionsByEntity["Tracker"].ContainsKey("Reset")).IsTrue();
    }
}