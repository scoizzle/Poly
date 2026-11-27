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
/// stack overflow cannot be caught, so every measurement runs in a child process: the probe is payload
/// only in the parent and acts when the child runner sets <c>POLY_CHILD_PROBE</c>. The chain must run
/// on both hosts up to the emitter's counted limit, and a chain past it — through Invoke or through an
/// initial stage's entry — must be the typed refusal, never an abort.
/// </summary>
[NotInParallel("ChildProcessSlots")]
public class InvokeChainDepthTests {
    /// <summary>The chain length the issue reports dying at (#1115).</summary>
    private const int IssueChain = 34;

    /// <summary>A chain well past the issue's, still under the emitter's limit: the compile must not depend on its length.</summary>
    private const int LongChain = 200;

    /// <summary>Chains past the emitter's counted limit; every path must refuse, never abort.</summary>
    private const int RefusedChain = 400;
    private const int RefusedChain1000 = 1000;
    private const int RefusedChain3000 = 3000;

    private const string ProbeVariable = "POLY_CHILD_PROBE";
    private const string LinksVariable = "POLY_CHILD_CHAIN_LINKS";
    private const string CompilesProbe = "invoke-chain";
    private const string RefusalProbe = "invoke-chain-refusal";
    private const string EntryRefusalProbe = "invoke-chain-entry-refusal";
    private const string DeepProbe = "invoke-chain-deep";
    private const string ParityProbe = "invoke-chain-parity";

    /// <summary>A chain of distinct actions, each invoking the next; the last one assigns.</summary>
    private static string Chain(int links) {
        var actions = new List<string>(links);
        for (var i = 1; i < links; i++)
            actions.Add($"  A{i}: action {{ invoke A{i + 1} }}");
        actions.Add($"  A{links}: action {{ assign N to 1 }}");
        return "domain Chain\nA: entity {\n  N: Number default(0)\n" + string.Join("\n", actions) + "\n}\n";
    }

    /// <summary>The same chain reached from the initial stage's entry, so <c>Create</c> compiles it.</summary>
    private static string EntryChain(int links) {
        var actions = new List<string>(links);
        for (var i = 1; i < links; i++)
            actions.Add($"  A{i}: action {{ invoke A{i + 1} }}");
        actions.Add($"  A{links}: action {{ assign N to 1 }}");
        return "domain Chain\nA: entity {\n  N: Number default(0)\n" + string.Join("\n", actions)
            + "\n  Draft: stage {\n    entry { invoke A1 }\n  }\n}\n";
    }

    private static ModuleInstanceExecutor Executor(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        // Apply, not ApplyAndAnalyze: the runtime refusal must be proved even when no analysis gate ran.
        var domain = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes).Root;
        return ModuleExec.For(domain);
    }

    private static void RunChain(int links) {
        var executor = Executor(Chain(links));

        var created = executor.Create("A", new Dictionary<string, object?>());
        if (!created.Succeeded)
            throw new InvalidOperationException("create A: " + created.Error);
        var outcome = executor.Invoke(created.Instance!, "A1");
        if (!outcome.Succeeded)
            throw new InvalidOperationException($"invoke A1 ({links} links): " + outcome.Error);
        if (Convert.ToInt64(created.Instance!.Bag["N"]) != 1L)
            throw new InvalidOperationException($"the last action did not assign; N = {created.Instance.Bag["N"]}");
    }

    private static int Links() =>
        int.Parse(Environment.GetEnvironmentVariable(LinksVariable) ?? RefusedChain.ToString());

    [Test]
    public async Task ChildProbe_InvokeChain_CompilesAndRuns() {
        if (Environment.GetEnvironmentVariable(ProbeVariable) != CompilesProbe)
            return;
        RunChain(Links());
    }

    /// <summary>A chain past the limit must be a failed <see cref="ModuleOutcome"/> naming the limit, not a throw.</summary>
    [Test]
    public async Task ChildProbe_InvokeChain_IsRefusedNotAborted() {
        if (Environment.GetEnvironmentVariable(ProbeVariable) != RefusalProbe)
            return;
        var links = Links();
        var executor = Executor(Chain(links));

        var created = executor.Create("A", new Dictionary<string, object?>());
        await Assert.That(created.Succeeded).IsTrue();
        var outcome = executor.Invoke(created.Instance!, "A1");

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.Error).Contains("invoke chain is too deep");
        await Assert.That(outcome.Error).Contains("Shorten the chain");
    }

    /// <summary>The initial stage's entry invokes the chain, so the refusal must surface from <c>Create</c>.</summary>
    [Test]
    public async Task ChildProbe_EntryInvokeChain_IsRefusedNotAborted() {
        if (Environment.GetEnvironmentVariable(ProbeVariable) != EntryRefusalProbe)
            return;
        var links = Links();
        var executor = Executor(EntryChain(links));

        var created = executor.Create("A", new Dictionary<string, object?>());

        await Assert.That(created.Succeeded).IsFalse();
        await Assert.That(created.Error).Contains("invoke chain is too deep");
        await Assert.That(created.Error).Contains("Shorten the chain");
    }

    /// <summary>A chain so long that analysis itself exhausts the parse stack must be a typed failure there, not
    /// an abort; when analysis survives, the emitter's counted limit refuses the compile.</summary>
    [Test]
    public async Task ChildProbe_DeepInvokeChain_IsATypedFailureNotAbort() {
        if (Environment.GetEnvironmentVariable(ProbeVariable) != DeepProbe)
            return;
        var links = Links();
        ModuleInstanceExecutor executor;
        try {
            executor = Executor(Chain(links));
        }
        catch (InsufficientExecutionStackException ex) {
            await Assert.That(ex.Message).Contains("remaining stack");
            return;
        }

        var created = executor.Create("A", new Dictionary<string, object?>());
        await Assert.That(created.Succeeded).IsTrue();
        var outcome = executor.Invoke(created.Instance!, "A1");
        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.Error).Contains("invoke chain is too deep");
    }

    /// <summary>The same chain on both hosts: the simulator and the printed C# module lower the same bodies, so a
    /// legal chain must run on each and leave the same state (the interpreter sim and the C# print share the
    /// Syntax module body).</summary>
    [Test]
    public async Task ChildProbe_InvokeChain_200Links_AgreesOnBothHosts() {
        if (Environment.GetEnvironmentVariable(ProbeVariable) != ParityProbe)
            return;
        var outcomes = await ParityScenario.FromDsl(Chain(LongChain), "InvokeChain200Links").AssertAgree(side => {
            side.Create("A", []);
            side.Invoke("A1");
        });

        await Assert.That(outcomes[1].Success).IsTrue();
        await Assert.That(outcomes[1].State["N"]).IsEqualTo("1");
    }

    private static async Task ChildSucceeds(string probe, string? links = null) {
        var env = new Dictionary<string, string?> { [ProbeVariable] = probe };
        if (links is not null)
            env[LinksVariable] = links;
        var result = await PolycProcess.RunTestInChildAsync(
            $"/*/*/{nameof(InvokeChainDepthTests)}/{ProbeName(probe)}",
            env);

        await Assert.That(result.IsAborted).IsFalse();
        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    private static string ProbeName(string probe) => probe switch {
        CompilesProbe => nameof(ChildProbe_InvokeChain_CompilesAndRuns),
        RefusalProbe => nameof(ChildProbe_InvokeChain_IsRefusedNotAborted),
        EntryRefusalProbe => nameof(ChildProbe_EntryInvokeChain_IsRefusedNotAborted),
        DeepProbe => nameof(ChildProbe_DeepInvokeChain_IsATypedFailureNotAbort),
        ParityProbe => nameof(ChildProbe_InvokeChain_200Links_AgreesOnBothHosts),
        _ => throw new ArgumentOutOfRangeException(nameof(probe), probe, null),
    };

    [Test]
    public async Task InvokeChain_34Links_CompilesAndRuns() =>
        await ChildSucceeds(CompilesProbe, IssueChain.ToString());

    [Test]
    public async Task InvokeChain_200Links_CompilesAndRuns() =>
        await ChildSucceeds(CompilesProbe, LongChain.ToString());

    [Test]
    public async Task InvokeChain_400Links_NeverAbortsTheProcess() =>
        await ChildSucceeds(RefusalProbe, RefusedChain.ToString());

    [Test]
    public async Task InvokeChain_1000Links_NeverAbortsTheProcess() =>
        await ChildSucceeds(RefusalProbe, RefusedChain1000.ToString());

    [Test]
    public async Task InvokeChain_3000Links_NeverAbortsTheProcess() =>
        await ChildSucceeds(DeepProbe, RefusedChain3000.ToString());

    [Test]
    public async Task InvokeChain_FromInitialEntry_400Links_NeverAbortsTheProcess() =>
        await ChildSucceeds(EntryRefusalProbe, RefusedChain.ToString());

    [Test]
    public async Task InvokeChain_200Links_AgreesOnBothHosts() =>
        await ChildSucceeds(ParityProbe);
}