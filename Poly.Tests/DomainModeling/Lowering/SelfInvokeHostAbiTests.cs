using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

public class SelfInvokeHostAbiTests {
    private static Entity CreateCartEntity() {
        var status = new Property("Status", new DomainTypeReference("Text"), []);
        var mark = new Poly.DomainModeling.Ontology.Action("MarkPaid", InvocationResult.Void, [],
            Effects: [new AssignEffect(
                DomainExpression.Property("Status"),
                DomainExpression.Literal("paid"))],
            Policies: []);
        var checkout = new Poly.DomainModeling.Ontology.Action("Checkout", InvocationResult.Void, [],
            Effects: [
                new AssignEffect(
                    DomainExpression.Property("Status"),
                    DomainExpression.Literal("checking")),
                new InvokeActionEffect("MarkPaid", [])
            ],
            Policies: []);
        return new Entity("Cart",
            Properties: [status],
            Actions: [checkout, mark],
            Policies: [],
            Stages: []);
    }

    [Test]
    public async Task SelfInvoke_LowersToInvokeMember_NotGatedOnFlag() {
        var entity = CreateCartEntity();
        var off = new EffectLoweringPass(entity, new LoweringContext(
            new Parameter("entity"), LowerStageTransitions: false));
        var on = new EffectLoweringPass(entity, new LoweringContext(
            new Parameter("entity"), LowerStageTransitions: true, UseThisReference: true));

        var effect = new InvokeActionEffect("Other", []);
        var loweredOff = off.TryLowerVmNode(effect);
        var loweredOn = on.TryLowerVmNode(effect);

        await Assert.That(loweredOff).IsNotNull();
        await Assert.That(loweredOn).IsNotNull();
        await Assert.That(loweredOff).IsTypeOf<Invoke>();
        await Assert.That(((Invoke)loweredOff!).Delegate).IsTypeOf<Member>();
        await Assert.That(((Member)((Invoke)loweredOff!).Delegate).MemberName).IsEqualTo("Other");
        await Assert.That(off.TryLowerVmNode(
            new InvokeActionEffect("Other", [], TargetRelationship: "orders"))).IsNull();
    }

    [Test]
    public async Task SelfInvoke_Runtime_UpdatesBagViaVmPath() {
        var entity = CreateCartEntity();
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Status"] = "open" });

        var result = instance.InvokeAction("Checkout");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.GetProperty<object>("Status")).IsEqualTo("paid");
    }

    [Test]
    public async Task Generate_InvokeMemberThisCheckout_PrintsThisCheckout() {
        var node = new Invoke(new Member(new ThisReference(), "Checkout"));
        var cs = new CSharpGenerator().Generate(node);
        await Assert.That(cs).IsEqualTo("this.Checkout();");
    }

    [Test]
    public async Task SelfInvoke_StageAction_Recursive_ExceedsDepth() {
        var bounce = new Poly.DomainModeling.Ontology.Action("Bounce", InvocationResult.Void, [],
            Effects: [new InvokeActionEffect("Bounce", [])],
            Policies: []);
        var draft = new Stage("Draft", [bounce], [], [], []);
        var entity = new Entity("Loop",
            Properties: [new Property("Status", new DomainTypeReference("Text"), [])],
            Actions: [],
            Policies: [],
            Stages: [draft]);
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Status"] = "x" });

        await Assert.That(() => instance.InvokeAction("Bounce"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("depth exceeded");
    }
}