using Poly.DomainModeling;
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

    const string UnknownNopeOnPerson = "Stage 'Nope' does not exist on entity 'Person'.";

    [Test]
    public async Task StageTransition_RuntimeContext_LowersToAssignmentWithoutInstanceNotify() {
        var entity = CreatePersonEntity();
        var context = LoweringFixtures.MinimalContext(new Parameter("entity", new TypeReference(entity.Name))) with { SourceStageName = "Draft" };
        var pass = new EffectLoweringPass(entity, context);

        var lowered = pass.TryLowerVmNode(new StageTransitionEffect(new StageReference("Active")));

        await Assert.That(lowered).IsNotNull();
        // Bare transition (no entry/exit/subscribers) is a lone Assignment, not a Block.
        var nodes = Flatten(lowered!).ToList();
        await Assert.That(nodes.Any(n =>
            n is Assignment {
                Destination: Member { MemberName: "CurrentStage" },
                Value: Member { MemberName: "Active" }
            })).IsTrue();
        // Instance Notify is simulator-only; lowering never emits it.
        await Assert.That(nodes.Any(n =>
            n is Invoke {
                Delegate: Member { MemberName: "Notify" }
            })).IsFalse();
        await Assert.That(nodes.Any(n => n is TryCatchFinally {
            FinallyBlock: Invoke { Delegate: Member { MemberName: "Notify" } }
        })).IsFalse();
    }

    [Test]
    public async Task StageTransition_LowersWithAndWithoutThisReference() {
        var entity = CreatePersonEntity();
        var off = new EffectLoweringPass(entity, LoweringFixtures.Context(new Parameter("entity"), entity));
        var on = new EffectLoweringPass(entity, LoweringFixtures.Context(new ThisReference(), entity));

        await Assert.That(off.TryLowerVmNode(new StageTransitionEffect(new StageReference("Active"))))
            .IsNotNull();
        await Assert.That(on.TryLowerVmNode(new StageTransitionEffect(new StageReference("Active"))))
            .IsNotNull();
    }

    [Test]
    public async Task Create_LowersOnRuntimePath() {
        var entity = CreatePersonEntity();
        var pass = new EffectLoweringPass(entity, LoweringFixtures.Context(new Parameter("entity"), entity));

        // Analysis is required now, so the old "no dictionary create" failure is unrepresentable; the legal path yields a typed create call.
        var created = pass.TryLowerVmNode(new CreateEntityInstance(new DomainTypeReference("Person")));
        await Assert.That(created).IsTypeOf<Block>();
        await Assert.That(created!.ToString()).Contains("Person.Create()");
        await Assert.That(pass.TryLowerVmNode(
            new InvokeActionEffect("Activate", [], TargetRelationship: "orders"))).IsNotNull();
        await Assert.That(pass.TryLowerVmNode(
            new ForEachInvokeEffect("orders", "x", null, "Activate", []))).IsNotNull();
    }

    [Test]
    public async Task InvokeAction_Transition_SetsStageWithoutEffectExecutor() {
        var entity = CreatePersonEntity();
        var domain = ValidDomain.Create("People", [entity]);
        var exec = ModuleExec.For(domain);
        var instance = exec.Create((entity).Name, new Dictionary<string, object?> { ["Name"] = "Alice" }).Instance!;

        var result = exec.Invoke(instance, "Activate");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(instance)).IsEqualTo("Active");
    }

    [Test]
    public async Task Lower_UnknownStageTransition_ThrowsFailClosed() {
        var entity = CreatePersonEntity();
        var pass = new EffectLoweringPass(entity, LoweringFixtures.Context(new Parameter("entity"), entity) with { SourceStageName = "Draft" });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            pass.TryLowerVmNode(new StageTransitionEffect(new StageReference("Nope"))));
        await Assert.That(ex!.Message).IsEqualTo(UnknownNopeOnPerson);
    }

    [Test]
    public async Task Export_Transition_SetsStageWithoutInstanceNotify() {
        var entity = CreatePersonEntity();
        var context = LoweringFixtures.MinimalContext(new ThisReference()) with { StageEnumTypeName = "PersonStage", SourceStageName = "Draft" };
        var pass = new EffectLoweringPass(entity, context);
        var lowered = pass.TryLowerVmNode(new StageTransitionEffect(new StageReference("Active")));
        var cs = new CSharpGenerator().Generate(lowered!);

        await Assert.That(cs).Contains("CurrentStage = PersonStage.Active");
        await Assert.That(cs).DoesNotContain("this.Notify(");
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