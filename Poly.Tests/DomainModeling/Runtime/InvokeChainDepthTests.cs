using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// #1115: a legal, acyclic invoke chain overflows the process when the simulator compiles its first
/// action (the ABI emitter inlines each callee's body, so compile depth grows with the chain). A
/// stack overflow cannot be caught, so the measurement runs in a child process: the probe is payload
/// only in the parent and acts when the child runner sets <c>POLY_CHILD_PROBE</c>. The chain must
/// compile and run — the issue's 34 links and a 200-link chain — and a chain too deep for the
/// compile thread must be a typed failure, never an abort.
/// </summary>
[NotInParallel("ChildProcessSlots")]
public class InvokeChainDepthTests {
    /// <summary>The chain length the issue reports dying at (#1115).</summary>
    private const int IssueChain = 34;

    /// <summary>A chain well past the issue's, still legal: the compile must not depend on its length.</summary>
    private const int LongChain = 200;

    private const string ProbeVariable = "POLY_CHILD_PROBE";
    private const string LinksVariable = "POLY_CHILD_CHAIN_LINKS";

    /// <summary>A chain of distinct actions, each invoking the next; the last one assigns.</summary>
    private static string Chain(int links) {
        var actions = new List<string>(links);
        for (var i = 1; i < links; i++)
            actions.Add($"  A{i}: action {{ invoke A{i + 1} }}");
        actions.Add($"  A{links}: action {{ assign N to 1 }}");
        return "domain Chain\nA: entity {\n  N: Number default(0)\n" + string.Join("\n", actions) + "\n}\n";
    }

    private static void RunChain(int links) {
        var changes = new PolyDslParser(Chain(links)).Parse();
        var domain = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes).Root;
        var executor = ModuleExec.For(domain);

        var created = executor.Create("A", new Dictionary<string, object?>());
        if (!created.Succeeded)
            throw new InvalidOperationException("create A: " + created.Error);
        var outcome = executor.Invoke(created.Instance!, "A1");
        if (!outcome.Succeeded)
            throw new InvalidOperationException($"invoke A1 ({links} links): " + outcome.Error);
        if (Convert.ToInt64(created.Instance!.Bag["N"]) != 1L)
            throw new InvalidOperationException($"the last action did not assign; N = {created.Instance.Bag["N"]}");
    }

    [Test]
    public async Task ChildProbe_InvokeChain_CompilesAndRuns() {
        if (Environment.GetEnvironmentVariable(ProbeVariable) is null)
            return;
        RunChain(int.Parse(Environment.GetEnvironmentVariable(LinksVariable) ?? IssueChain.ToString()));
    }

    private static async Task CompilesAndRuns(int links) {
        var result = await PolycProcess.RunTestInChildAsync(
            $"/*/*/{nameof(InvokeChainDepthTests)}/{nameof(ChildProbe_InvokeChain_CompilesAndRuns)}",
            new Dictionary<string, string?> {
                [ProbeVariable] = "invoke-chain",
                [LinksVariable] = links.ToString(),
            });

        await Assert.That(result.IsAborted).IsFalse();
        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    [Test]
    public async Task InvokeChain_34Links_CompilesAndRuns() => await CompilesAndRuns(IssueChain);

    [Test]
    public async Task InvokeChain_200Links_CompilesAndRuns() => await CompilesAndRuns(LongChain);

    /// <summary>The same chain on both hosts: the simulator and the printed C# module lower the same bodies, so a
    /// legal chain must run on each and leave the same state (the interpreter sim and the C# print share the
    /// Syntax module body).</summary>
    [Test]
    public async Task InvokeChain_200Links_AgreesOnBothHosts() {
        var outcomes = await ParityScenario.FromDsl(Chain(LongChain), "InvokeChain200Links").AssertAgree(side => {
            side.Create("A", []);
            side.Invoke("A1");
        });

        await Assert.That(outcomes[1].Success).IsTrue();
        await Assert.That(outcomes[1].State["N"]).IsEqualTo("1");
    }
}