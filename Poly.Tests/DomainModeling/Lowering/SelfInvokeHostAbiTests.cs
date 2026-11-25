using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
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
        var off = new EffectLoweringPass(entity, LoweringFixtures.Context(new Parameter("entity"), entity));
        var on = new EffectLoweringPass(entity, LoweringFixtures.Context(new ThisReference(), entity));

        var effect = new InvokeActionEffect("Other", []);
        var loweredOff = off.TryLowerVmNode(effect);
        var loweredOn = on.TryLowerVmNode(effect);

        await Assert.That(loweredOff).IsNotNull();
        await Assert.That(loweredOn).IsNotNull();
        var invoke = FindInvoke(loweredOff!);
        await Assert.That(invoke).IsNotNull();
        await Assert.That(invoke!.Delegate).IsTypeOf<Member>();
        await Assert.That(((Member)invoke.Delegate).MemberName).IsEqualTo("Other");
        await Assert.That(off.TryLowerVmNode(
            new InvokeActionEffect("Other", [], TargetRelationship: "orders"))).IsNotNull();
    }

    [Test]
    public async Task SelfInvoke_Runtime_UpdatesBagViaVmPath() {
        var entity = CreateCartEntity();
        var domain = ValidDomain.Create("Shop", [entity]);
        var exec = ModuleExec.For(domain);
        var instance = exec.Create((entity).Name, new Dictionary<string, object?> { ["Status"] = "open" }).Instance!;

        var result = exec.Invoke(instance, "Checkout");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.Bag["Status"]).IsEqualTo("paid");
    }

    [Test]
    public async Task Generate_InvokeMemberThisCheckout_PrintsThisCheckout() {
        var node = new Invoke(new Member(new ThisReference(), "Checkout"));
        var cs = new CSharpGenerator().Generate(node);
        await Assert.That(cs).IsEqualTo("this.Checkout();");
    }

    [Test]
    public async Task SelfInvoke_RuntimeDepthGuard_ExceedsDepth() {
        // #1101: the analyzer refuses every recursive invoke cycle (DMEFF021), and an accepted chain of
        // distinct actions cannot reach the guard: the ABI emitter inlines each callee's body, so a chain
        // overflows the compiler's own stack at about 34 links (33 links lower and run without overflowing
        // the VM). No accepted model therefore recurses. The guard's contract is the VM value-stack
        // overflow translation, so this executor-level test installs the recursive body the analyzer would
        // refuse into an already-lowered module and drives that guard directly.
        var tick = new Poly.DomainModeling.Ontology.Action("Tick", InvocationResult.Void, [], [], []);
        var entity = new Entity("Loop", Properties: [], Actions: [tick], Policies: [], Stages: []);
        var domain = ValidDomain.Create("Loops", [entity]);
        _ = ModuleExec.For(domain);

        var recursive = new EffectLoweringPass(entity, LoweringFixtures.Context(new ThisReference(), entity))
            .TryLowerVmNode(new InvokeActionEffect("Tick", []));
        await Assert.That(recursive).IsNotNull();
        var module = RuntimeAnalysisCache.GetOrLower(domain);
        RuntimeAnalysisCache.ReplaceModule(domain, [.. module.Select(type => type.Name != "Loop" ? type : type with {
            Methods = [.. type.Methods!.Select(m => m.Name == "Tick" ? m with { Body = recursive } : m)],
        })]);

        var exec = new ModuleInstanceExecutor(domain);
        var instance = exec.Create("Loop", new Dictionary<string, object?>()).Instance!;

        var result = exec.Invoke(instance, "Tick");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("depth exceeded");
    }

    /// <summary>#1101: the guarded self-invoke this class's depth guard used to accept is now a DMEFF021 error,
    /// so the refusal is pinned where that acceptance was.</summary>
    [Test]
    public async Task SelfInvoke_StageAction_GuardedRecursion_IsRefused_1101() {
        var bounce = new Poly.DomainModeling.Ontology.Action("Bounce", InvocationResult.Void, [],
            Effects: [
                new ConditionalEffect(
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("N"), DomainExpression.Literal(0L)),
                    [new InvokeActionEffect("Bounce", [])],
                    null)
            ],
            Policies: []);
        var draft = new Stage("Draft", [bounce], [], [], []);
        var entity = new Entity("Loop",
            Properties: [
                new Property("Status", new DomainTypeReference("Text"), []),
                new Property("N", new DomainTypeReference("Number"), []),
            ],
            Actions: [],
            Policies: [],
            Stages: [draft]);
        var domain = ValidDomain.Create("Loops", [entity]);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.HasErrors).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.InvokeCycle)).IsTrue();
    }

    private static Invoke? FindInvoke(Node node) {
        if (node is Invoke inv) return inv;
        foreach (var child in node.Children) {
            if (child is null) continue;
            var found = FindInvoke(child);
            if (found is not null) return found;
        }
        return null;
    }
}