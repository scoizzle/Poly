using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

public class CrossEntityInvokeHostAbiTests {
    private static (Entity orchestrator, Entity service, Domain domain) CreateLinkedDomain() {
        var status = new Property("Status", new DomainTypeReference("Text"), []);
        var service = new Entity("Service", [status], Actions: [
            new Poly.DomainModeling.Ontology.Action("Process", InvocationResult.Void, [], [
                new AssignEffect(DomainExpression.Property("Status"),
                    DomainExpression.Literal("processed"))
            ], [])
        ], [], []);
        var orchestrator = new Entity("Orchestrator", [], Actions: [
            new Poly.DomainModeling.Ontology.Action("Run", InvocationResult.Void, [], [
                new InvokeActionEffect("Process", [], TargetRelationship: "ServiceCall")
            ], [])
        ], [], []);
        var rel = new Relationship("ServiceCall",
            new DomainTypeReference("Orchestrator"), new DomainTypeReference("Service"),
            RelationshipCardinality.OneToOne, []);
        var domain = ValidDomain.Create("Test", [orchestrator, service], [rel]);
        return (orchestrator, service, domain);
    }

    [Test]
    public async Task CrossEntityInvoke_LowersToNavCall_NotGatedOnFlag() {
        var (orchestrator, _, _) = CreateLinkedDomain();
        var off = new EffectLoweringPass(orchestrator, new LoweringContext(
            new Parameter("entity")));
        var on = new EffectLoweringPass(orchestrator, new LoweringContext(
            new ThisReference()));

        var effect = new InvokeActionEffect("Process", [], TargetRelationship: "ServiceCall");
        var loweredOff = off.TryLowerVmNode(effect);
        var loweredOn = on.TryLowerVmNode(effect);

        await Assert.That(loweredOff).IsNotNull();
        await Assert.That(loweredOn).IsNotNull();
        await Assert.That(off.TryLowerVmNode(new InvokeActionEffect("Process", []))).IsNotNull();
    }

    [Test]
    public async Task CrossEntityInvoke_Runtime_InvokesOnLinkedTarget() {
        var (orchestrator, service, domain) = CreateLinkedDomain();
        var exec = ModuleExec.For(domain);
        var svc = exec.Create((service).Name, new Dictionary<string, object?> { ["Status"] = "idle" }).Instance!;
        var orch = exec.Create((orchestrator).Name, new Dictionary<string, object?>()).Instance!;
        exec.Link("ServiceCall", orch, svc);

        var result = exec.Invoke(orch, "Run");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(svc.Bag["Status"]).IsEqualTo("processed");
    }

    [Test]
    public async Task CrossEntityInvoke_Unlinked_FailsWithDomainMessage() {
        var (orchestrator, _, domain) = CreateLinkedDomain();
        var exec = ModuleExec.For(domain);
        var orch = exec.Create((orchestrator).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(orch, "Run");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("requires a linked 'ServiceCall'");
    }

    [Test]
    public async Task Generate_CrossEntityInvoke_PrintsNavGuardAndCall() {
        var nav = new Member(new ThisReference(), "ServiceCall");
        var node = new Block([
            new IfStatement(
                new Equal(nav, new Constant(null!)),
                new Block([new Return(new Invoke(
                    new Member(new TypeReference("DomainResult"), "Failure"),
                    new Constant("'Process' requires a linked 'ServiceCall' on entity 'Orchestrator'.")))])),
            new Invoke(new Member(nav, "Process"))
        ]);
        var cs = new CSharpGenerator().Generate(node);
        await Assert.That(cs).Contains("this.ServiceCall == null");
        await Assert.That(cs).Contains("DomainResult.Failure(\"'Process' requires a linked 'ServiceCall' on entity 'Orchestrator'.\")");
        await Assert.That(cs).Contains("this.ServiceCall.Process()");
        await Assert.That(cs).DoesNotContain("this.ServiceCall!");
    }
}