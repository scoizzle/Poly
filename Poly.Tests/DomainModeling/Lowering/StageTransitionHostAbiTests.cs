using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

public class StageTransitionHostAbiTests {
    private static Entity CreatePersonEntity() {
        var name = new Property("Name", new DomainTypeReference("Text"), []);
        var activate = new Poly.DomainModeling.Ontology.Action("Activate", InvocationResult.Void, [],
            [new StageTransitionEffect(new StageReference("Active"))], []);
        var draft = new Stage("Draft",
            Actions: [activate],
            Policies: [], OnEntryEffects: [], OnExitEffects: []);
        var activeStage = new Stage("Active",
            Actions: [],
            Policies: [], OnEntryEffects: [], OnExitEffects: []);
        return new Entity("Person",
            Properties: [name],
            Actions: [activate],
            Policies: [],
            Stages: [draft, activeStage]);
    }

    [Test]
    public async Task StageTransition_RuntimeContext_LowersToAssignmentAndInvokeNotify() {
        var entity = CreatePersonEntity();
        var context = new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name)),
            SourceStageName: "Draft");
        var pass = new EffectLoweringPass(entity, context);

        var lowered = pass.TryLowerVmNode(new StageTransitionEffect(new StageReference("Active")));

        await Assert.That(lowered).IsNotNull();
        await Assert.That(lowered).IsTypeOf<Block>();
        var nodes = Flatten(lowered!).ToList();
        await Assert.That(nodes.Any(n =>
            n is Assignment {
                Destination: Member { MemberName: "CurrentStage" },
                Value: Constant { Value: "Active" }
            })).IsTrue();
        await Assert.That(nodes.Any(n =>
            n is Invoke {
                Delegate: Member { MemberName: "Notify" }
            } inv && inv.Arguments is [Constant { Value: "Active" }])).IsTrue();
        await Assert.That(nodes.Any(n => n is TryCatchFinally {
            FinallyBlock: Invoke { Delegate: Member { MemberName: "Notify" } }
        })).IsTrue();
    }

    [Test]
    public async Task StageTransition_IsNotGatedOnLowerStageTransitionsFlag() {
        var entity = CreatePersonEntity();
        var off = new EffectLoweringPass(entity, new LoweringContext(
            new Parameter("entity"), LowerStageTransitions: false));
        var on = new EffectLoweringPass(entity, new LoweringContext(
            new Parameter("entity"), LowerStageTransitions: true, UseThisReference: true));

        await Assert.That(off.TryLowerVmNode(new StageTransitionEffect(new StageReference("Active"))))
            .IsNotNull();
        await Assert.That(on.TryLowerVmNode(new StageTransitionEffect(new StageReference("Active"))))
            .IsNotNull();
    }

    [Test]
    public async Task CreateAndInvoke_StillNullOnRuntimePath() {
        var entity = CreatePersonEntity();
        var pass = new EffectLoweringPass(entity, new LoweringContext(
            new Parameter("entity"), LowerStageTransitions: false));

        await Assert.That(pass.TryLowerVmNode(
            new CreateEntityInstance(new DomainTypeReference("Person")))).IsNull();
        await Assert.That(pass.TryLowerVmNode(
            new InvokeActionEffect("Activate", [], TargetRelationship: "orders"))).IsNull();
        await Assert.That(pass.TryLowerVmNode(
            new ForEachInvokeEffect("orders", "x", null, "Activate", []))).IsNull();
    }

    [Test]
    public async Task InvokeAction_Transition_SetsStageWithoutEffectExecutor() {
        var entity = CreatePersonEntity();
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Name"] = "Alice" });

        var result = instance.InvokeAction("Activate");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.CurrentStage).IsEqualTo("Active");
    }

    [Test]
    public async Task Export_Transition_EmitsNotifyCallAndCompilesShape() {
        var entity = CreatePersonEntity();
        var context = new LoweringContext(
            new ThisReference(),
            UseThisReference: true,
            LowerStageTransitions: true,
            StageEnumTypeName: "PersonStage",
            SourceStageName: "Draft");
        var pass = new EffectLoweringPass(entity, context);
        var lowered = pass.TryLowerVmNode(new StageTransitionEffect(new StageReference("Active")));
        var cs = new CSharpGenerator().Generate(lowered!);

        await Assert.That(cs).Contains("CurrentStage = PersonStage.Active");
        await Assert.That(cs).Contains("this.Notify(\"Active\")");
        await Assert.That(cs).DoesNotContain("/*");
        await Assert.That(cs).DoesNotContain("throw");
    }

    private static IEnumerable<Node> Flatten(Node node) {
        yield return node;
        switch (node) {
            case Block b:
                foreach (var child in b.Nodes)
                    foreach (var n in Flatten(child))
                        yield return n;
                break;
            case TryCatchFinally t:
                foreach (var n in Flatten(t.TryBlock))
                    yield return n;
                if (t.FinallyBlock is not null)
                    foreach (var n in Flatten(t.FinallyBlock))
                        yield return n;
                break;
        }
    }
}