using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation;

namespace Poly.Tests.DomainModeling.Lowering;

public class ForEachInvokeHostAbiTests {
    [Test]
    public async Task VmForEach_OverObjectList_RunsBodyPerItem() {
        var sum = new Variable("sum");
        var item = new Variable("item");
        var node = new Block([
            new Assignment(sum, new Constant(0L)),
            new ForEachLoop(
                item,
                new Constant(new object[] { "a", "b", "c" }),
                new Assignment(sum, new Poly.Ast.Nodes.Add(sum, new Constant(1L)))),
            sum
        ], [sum]);
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.Result.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task ForEachInvoke_Lowers_NotGatedOnFlag() {
        var source = new Entity("Source", [], Actions: [
            new Poly.DomainModeling.Ontology.Action("Run", InvocationResult.Void, [], [
                new ForEachInvokeEffect("Items", "item", null, "Process", [])
            ], [])
        ], [], []);
        var off = new EffectLoweringPass(source, new LoweringContext(
            new Parameter("entity")));
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
        var domain = ValidDomain.Create("Test", [source, target], [rel]);
        var exec = ModuleExec.For(domain);
        var tgt1 = exec.Create((target).Name, new Dictionary<string, object?> { ["Status"] = "a" }).Instance!;
        var tgt2 = exec.Create((target).Name, new Dictionary<string, object?> { ["Status"] = "b" }).Instance!;
        var src = exec.Create((source).Name, new Dictionary<string, object?>()).Instance!;
        exec.Link("Items", src, tgt1);
        exec.Link("Items", src, tgt2);

        var result = exec.Invoke(src, "RunAll");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(tgt1.Bag["Status"]).IsEqualTo("done");
        await Assert.That(tgt2.Bag["Status"]).IsEqualTo("done");
    }

    [Test]
    public async Task CollectionNav_ManyToMany_IDictionaryReturnsLinkedTargets() {
        var target = new Entity("Target", [], [], [], []);
        var source = new Entity("Source", [], [], [], []);
        var rel = new Relationship("Peers",
            new DomainTypeReference("Source"), new DomainTypeReference("Target"),
            RelationshipCardinality.ManyToMany, []);
        var domain = ValidDomain.Create("Test", [source, target], [rel]);
        var exec = ModuleExec.For(domain);
        var tgt = exec.Create((target).Name, new Dictionary<string, object?>()).Instance!;
        var src = exec.Create((source).Name, new Dictionary<string, object?>()).Instance!;
        exec.Link("Peers", src, tgt);

        await Assert.That(exec.IsLinked("Peers", src, tgt)).IsTrue();
        await Assert.That(exec.LinkedTargets("Peers", src).Single().Id).IsEqualTo(tgt.Id);
    }
}