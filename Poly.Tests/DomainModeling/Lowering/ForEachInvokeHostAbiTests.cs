using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation;

namespace Poly.Tests.DomainModeling.Lowering;

public class ForEachInvokeHostAbiTests {
    [Test]
    public async Task VmForEach_OverObjectList_RunsBodyPerItem() {
        var sum = new Variable("sum", new Constant(0L));
        var item = new Variable("item");
        var node = new Block([
            sum,
            new ForEachLoop(
                item,
                new Constant(new object[] { "a", "b", "c" }),
                new Assignment(sum, new Poly.Ast.Nodes.Add(sum, new Constant(1L)))),
            sum
        ]);
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.Result.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task VmForEach_DomainInstance_CallsNotify() {
        var entity = new Entity("E",
            [new Property("S", new DomainTypeReference("Text"), [])],
            [], [], []);
        var inst = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["S"] = "a" });
        var item = new Variable("item");
        var node = new ForEachLoop(
            item,
            new Constant(new object[] { inst }),
            new Invoke(new Member(item, "Notify"), new Constant("Draft")));
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec).IsNotNull();
    }

    [Test]
    public async Task VmForEach_DomainInstance_InvokeNamedProcess() {
        var status = new Property("Status", new DomainTypeReference("Text"), []);
        var target = new Entity("Target", [status], Actions: [
            new Poly.DomainModeling.Ontology.Action("Process", InvocationResult.Void, [], [
                new AssignEffect(DomainExpression.Property("Status"),
                    DomainExpression.Literal("done"))
            ], [])
        ], [], []);
        var inst = DomainEntityInstance.Create(target,
            new Dictionary<string, object?> { ["Status"] = "a" });
        var item = new Variable("item");
        var node = new ForEachLoop(
            item,
            new Constant(new object[] { inst }),
            new Invoke(new Member(item, "Process")));
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program);
        await Assert.That(inst.GetProperty<object>("Status")).IsEqualTo("done");
    }

    [Test]
    public async Task ForEachInvoke_Lowers_NotGatedOnFlag() {
        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Ontology.Action("Run", InvocationResult.Void, [], [
                new ForEachInvokeEffect("Items", "item", null, "Process", [])
            ], [])
        ], [], []);
        var off = new EffectLoweringPass(source, new LoweringContext(
            new Parameter("entity"), LowerStageTransitions: false));
        var lowered = off.TryLowerVmNode(
            new ForEachInvokeEffect("Items", "item", null, "Process", []));
        await Assert.That(lowered).IsNotNull();
        await Assert.That(lowered).IsTypeOf<Block>();
    }

    [Test]
    public async Task ForEachInvoke_Runtime_InvokesOnEveryTarget() {
        var status = new Property("Status", new DomainTypeReference("Text"), []);
        var target = new Entity("Target", [status], Actions: [
            new Poly.DomainModeling.Ontology.Action("Process", InvocationResult.Void, [], [
                new AssignEffect(DomainExpression.Property("Status"),
                    DomainExpression.Literal("done"))
            ], [])
        ], [], []);
        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Ontology.Action("RunAll", InvocationResult.Void, [], [
                new ForEachInvokeEffect("Items", "item", null, "Process", [])
            ], [])
        ], [], []);
        var rel = new Relationship("Items",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Test", [source, target], [rel]);
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
}