using System.Text;

using Poly.Tests.TestHelpers;

namespace Poly.Tests.Mcp;

/// <summary>
/// #1115 round 4: <c>poly_define</c> analyzes on the 16 MB parse stack, so it accepts a nested invoke
/// chain that <c>poly_simulate</c> used to re-analyze and lower on the request thread, where the same
/// document hit the thread's remaining stack and failed even the first <c>create</c> step. The
/// simulator's execution compile now hops to the parse stack too, so a define-accepted document either
/// runs or gets the documented invoke-chain-too-deep refusal.
/// </summary>
public class SimulateDeepChainTests {
    /// <summary>A chain of distinct actions, each invoking the next inside <paramref name="nest"/> nested ifs.</summary>
    private static string NestedChain(int links, int nest) {
        var open = string.Concat(Enumerable.Repeat("if (N >= 0) { ", nest));
        var close = string.Concat(Enumerable.Repeat(" }", nest));
        var sb = new StringBuilder("domain Chain\nA: entity {\n  N: Number default(0)\n");
        for (var i = 1; i < links; i++)
            sb.Append($"  A{i}: action {{ {open}invoke A{i + 1}{close} }}\n");
        sb.Append($"  A{links}: action {{ assign N to 1 }}\n}}\n");
        return sb.ToString();
    }

    private static async Task AssertRunsOrRefuses(HarnessStep create, HarnessStep? invoke, string label) {
        if (!create.Ok) {
            await Assert.That(create.Summary).Contains("invoke chain").Because(label);
            await Assert.That(create.Summary).Contains("shorten").Because(label);
            return;
        }
        if (invoke is null)
            return;
        if (!invoke.Ok) {
            await Assert.That(invoke.Summary).Contains("invoke chain").Because(label);
            await Assert.That(invoke.Summary).Contains("shorten").Because(label);
        }
    }

    [Test]
    [Arguments(256, 12)]
    [Arguments(200, 20)]
    public async Task Simulate_DefineAcceptedNestedChain_CreateSucceeds(int links, int nest) {
        var harness = PolySurfaceHarness.New();
        var workspace = harness.Open();
        var defined = harness.Define(workspace, NestedChain(links, nest));
        await Assert.That(defined.Success).IsTrue().Because(defined.Summary);

        var created = harness.Create(workspace, "A");
        await Assert.That(created.Summary).DoesNotContain("thread with a larger stack");
        await Assert.That(created.Ok).IsTrue().Because(created.Summary);
    }

    [Test]
    [Arguments(256, 12)]
    [Arguments(200, 20)]
    public async Task Simulate_DefineAcceptedNestedChain_InvokeRunsOrRefusesTyped(int links, int nest) {
        var harness = PolySurfaceHarness.New();
        var workspace = harness.Open();
        var defined = harness.Define(workspace, NestedChain(links, nest));
        await Assert.That(defined.Success).IsTrue().Because(defined.Summary);

        var created = harness.Create(workspace, "A");
        await Assert.That(created.Summary).DoesNotContain("thread with a larger stack");
        if (!created.Ok) {
            await AssertRunsOrRefuses(created, null, "create refusal");
            return;
        }

        var invoked = harness.Invoke(workspace, created.InstanceId, "A1");
        await Assert.That(invoked.Summary).DoesNotContain("thread with a larger stack");
        await AssertRunsOrRefuses(created, invoked, "invoke refusal");
    }
}