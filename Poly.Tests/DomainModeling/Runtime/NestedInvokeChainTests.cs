using Poly.DomainModeling.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// #1115 round 3: the counted invoke-chain limit bounds callables, not the stack a link spends. A chain under
/// that limit whose invoke sits inside nested branches is thousands of frames deep and used to abort the process
/// on the 16 MB compile stack. Every measurement runs in a child process because a stack overflow cannot be
/// caught: the probe is payload only in the parent and acts when the child runner sets <c>POLY_CHILD_PROBE</c>.
/// The child accepts success or the typed refusal, and the parent proves it was never aborted.
/// </summary>
[NotInParallel("ChildProcessSlots")]
public class NestedInvokeChainTests {
    private const string Probe = "nested-invoke-chain";
    private const string ProbeVariable = "POLY_CHILD_PROBE";
    private const string LinksVariable = "POLY_CHILD_CHAIN_LINKS";
    private const string NestVariable = "POLY_CHILD_CHAIN_NEST";

    /// <summary>A chain of distinct actions, each invoking the next inside <paramref name="nest"/> nested ifs.</summary>
    private static string NestedChain(int links, int nest) {
        var actions = new List<string>(links);
        var open = string.Concat(Enumerable.Repeat("if (N >= 0) { ", nest));
        var close = string.Concat(Enumerable.Repeat(" }", nest));
        for (var i = 1; i < links; i++)
            actions.Add($"  A{i}: action {{ {open}invoke A{i + 1}{close} }}");
        actions.Add($"  A{links}: action {{ assign N to 1 }}");
        return "domain Chain\nA: entity {\n  N: Number default(0)\n" + string.Join("\n", actions) + "\n}\n";
    }

    /// <summary>
    /// Runs the define-accepted path (<see cref="EvolvedDomain.FromDsl"/> analyzes) and then the simulator's
    /// Create/Invoke. Analysis, lowering or compile may each be the typed stack refusal; success is also legal.
    /// </summary>
    [Test]
    public async Task ChildProbe_NestedInvokeChain_RunsOrRefusesTyped() {
        if (Environment.GetEnvironmentVariable(ProbeVariable) != Probe)
            return;
        var links = int.Parse(Environment.GetEnvironmentVariable(LinksVariable)!);
        var nest = int.Parse(Environment.GetEnvironmentVariable(NestVariable)!);
        try {
            var (domain, _) = EvolvedDomain.FromDsl(NestedChain(links, nest));
            var executor = ModuleExec.For(domain);
            var created = executor.Create("A", new Dictionary<string, object?>());
            if (!created.Succeeded) {
                await Assert.That(created.Error).Contains("invoke chain");
                await Assert.That(created.Error).Contains("too deep");
                return;
            }
            var outcome = executor.Invoke(created.Instance!, "A1");
            // Either it runs, or it is the typed refusal; an abort fails the parent.
            if (!outcome.Succeeded) {
                await Assert.That(outcome.Error).Contains("invoke chain");
                await Assert.That(outcome.Error).Contains("too deep");
            }
        }
        catch (InsufficientExecutionStackException) {
            // The analysis or lowering pass stopped with its typed stack failure: not an abort.
        }
    }

    private static async Task ChildSucceeds(int links, int nest) {
        var result = await PolycProcess.RunTestInChildAsync(
            $"/*/*/{nameof(NestedInvokeChainTests)}/{nameof(ChildProbe_NestedInvokeChain_RunsOrRefusesTyped)}",
            new Dictionary<string, string?> {
                [ProbeVariable] = Probe,
                [LinksVariable] = links.ToString(),
                [NestVariable] = nest.ToString(),
            });

        await Assert.That(result.IsAborted).IsFalse();
        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    // A 256-link chain at every nesting depth the DSL's statement limit allows in the small.
    [Test]
    [Arguments(256, 1)]
    [Arguments(256, 2)]
    [Arguments(256, 3)]
    [Arguments(256, 4)]
    [Arguments(256, 5)]
    [Arguments(256, 6)]
    [Arguments(256, 7)]
    [Arguments(256, 8)]
    [Arguments(256, 9)]
    [Arguments(256, 10)]
    [Arguments(256, 11)]
    [Arguments(256, 12)]
    public async Task NestedInvokeChain_256Links_NeverAbortsTheProcess(int links, int nest) =>
        await ChildSucceeds(links, nest);

    // Around and past the counted limit at a nesting depth that aborted Release builds before the fix.
    [Test]
    [Arguments(120, 5)]
    [Arguments(150, 5)]
    [Arguments(250, 5)]
    [Arguments(256, 5)]
    [Arguments(257, 5)]
    [Arguments(300, 5)]
    public async Task NestedInvokeChain_LinkCounts_NeverAbortTheProcess(int links, int nest) =>
        await ChildSucceeds(links, nest);

    // A plain chain: the counted limit still refuses cheaply, and a long one must never abort.
    [Test]
    [Arguments(256)]
    [Arguments(257)]
    [Arguments(1000)]
    [Arguments(10000)]
    public async Task PlainInvokeChain_NeverAbortsTheProcess(int links) =>
        await ChildSucceeds(links, 0);
}