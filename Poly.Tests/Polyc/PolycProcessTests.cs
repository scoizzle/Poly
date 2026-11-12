using System.Runtime.CompilerServices;

using Poly.Tests.Polyc;

namespace Poly.Tests.TestHelpers;

/// <summary>The child-process runner's invariant: acquire slot, start, drain (bounded, chunked), kill the tree,
/// bounded wait, release. The <c>ChildProbe_*</c> fixtures are payload only: they do nothing in the parent and act
/// only when the parent of a filtered child run sets <c>POLY_CHILD_PROBE</c>. They never call <see cref="PolycProcess"/>.</summary>
[NotInParallel("ChildProcessSlots")]
public class PolycProcessTests {
    /// <summary>Length of a <c>not not ... Flag is true</c> chain. Measured on net10.0/macOS: <c>polyc check</c> aborts with exit 134
    /// ("Stack overflow." in Poly.Grammar.Matcher) at 2000, 5000, 10000, 20000, 50000 and 400000. Parenthesis nesting does not
    /// overflow: it stalls past 30 s (2000 deep). When the parser is hardened (G-10) this test flips; the mechanism's permanent
    /// proof is <see cref="RunTestInChild_OverflowingChild_ReportsAbortNotHostCrash"/>.</summary>
    private const int OverflowDepth = 20_000;

    private static IReadOnlyDictionary<string, string?> Probe(string name, string? holdMs = null) =>
        new Dictionary<string, string?> { ["POLY_CHILD_PROBE"] = name, ["POLY_CHILD_HOLD_MS"] = holdMs };

    private static string ProbeFilter(string test) => $"/*/*/{nameof(PolycProcessTests)}/{test}";

    [Test]
    public async Task Run_TimesOut_KillsChild() {
        var (dll, _) = PolycProcess.HostFiles("polyc");
        var pid = 0;
        var spec = new ChildSpec(dll, ["mcp"]) { Timeout = TimeSpan.FromSeconds(2), Started = p => pid = p };

        var ex = await Assert.ThrowsAsync<PolycTimeoutException>(() => PolycProcess.RunAsync(spec));

        await Assert.That(ex!.Killed).IsTrue();
        await Assert.That(pid).IsGreaterThan(0);
        await Assert.That(PackOracle.IsProcessAlive(pid)).IsFalse();
    }

    [Test]
    public async Task Check_CrashingChild_ReportsSigabrtExitCodeAsResult() {
        var dir = PolycTestSupport.TempDir();
        try {
            var path = Path.Combine(dir, "deep.poly");
            File.WriteAllText(path, "domain Deep\n\nA: entity {\n  Flag: Boolean\n  Ok: policy { " + string.Concat(Enumerable.Repeat("not ", OverflowDepth)) + "Flag is true }\n}\n");

            var result = await PolycProcess.CheckAsync(path);

            await Assert.That(result.IsAborted).IsTrue();
            await Assert.That(result.ExitCode).IsEqualTo(134);
            await Assert.That(result.Stderr).Contains("Stack overflow");
        }
        finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task RunTestInChild_FilteredTest_ReturnsItsExitCode() {
        var ok = await PolycProcess.RunTestInChildAsync(ProbeFilter(nameof(ChildProbe_Returns)), Probe("return"));
        var bad = await PolycProcess.RunTestInChildAsync(ProbeFilter(nameof(ChildProbe_Fails)), Probe("fail"));

        await Assert.That(ok.ExitCode).IsEqualTo(0);
        await Assert.That(bad.ExitCode).IsNotEqualTo(0);
        await Assert.That(bad.IsAborted).IsFalse();
    }

    [Test]
    public async Task McpStdio_InitializeThenToolsList_RoundTrips() {
        using var host = await PolycProcess.McpAsync(McpTarget.Polyc);
        await host.InitializeAsync();
        var tools = await host.ToolsListAsync();

        await Assert.That(host.Instructions).IsNotNull();
        await Assert.That(host.Instructions!).Contains("poly_guide");
        await Assert.That(tools.Select(t => t["name"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray()).IsEquivalentTo(
            new[] { "poly_check", "poly_define", "poly_export", "poly_guide", "poly_inspect", "poly_simulate", "poly_workspace" });
    }

    [Test]
    public async Task RunTestInChild_OverflowingChild_ReportsAbortNotHostCrash() {
        var result = await PolycProcess.RunTestInChildAsync(ProbeFilter(nameof(ChildProbe_OverflowsStack)), Probe("overflow-probe"));

        await Assert.That(result.IsAborted).IsTrue();
        await Assert.That(result.ExitCode).IsEqualTo(134);
    }

    [Test]
    public async Task Run_ChildFloodsStdoutAndStderr_IsBoundedAndCompletes() {
        var result = await PolycProcess.RunTestInChildAsync(ProbeFilter(nameof(ChildProbe_Floods)), Probe("flood"));

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Stdout.Length).IsLessThanOrEqualTo(PolycProcess.StdoutCapBytes);
        await Assert.That(result.StdoutDroppedChars).IsGreaterThan(0L);
        await Assert.That(result.StderrDroppedChars).IsGreaterThan(0L);
    }

    [Test]
    public async Task Run_Cancel_KillsChild() {
        using var cts = new CancellationTokenSource();
        var pid = 0;
        var spec = HoldSpec(p => {
            pid = p;
            cts.CancelAfter(TimeSpan.FromMilliseconds(200));
        });

        await Assert.ThrowsAsync<OperationCanceledException>(() => PolycProcess.RunAsync(spec, cts.Token));

        await Assert.That(pid).IsGreaterThan(0);
        await Assert.That(PackOracle.IsProcessAlive(pid)).IsFalse();
    }

    [Test]
    public async Task Slots_NeverExceedTwoLiveChildren() {
        var pids = new List<int>();
        var maxAlive = 0;
        void OnStarted(int pid) {
            lock (pids) {
                pids.Add(pid);
                maxAlive = Math.Max(maxAlive, pids.Count(PackOracle.IsProcessAlive));
            }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PolycProcess.RunAsync(HoldSpec(OnStarted, holdMs: "1500"))));

        await Assert.That(results.All(r => r.ExitCode == 0)).IsTrue();
        await Assert.That(pids.Count).IsEqualTo(4);
        await Assert.That(maxAlive).IsLessThanOrEqualTo(ChildProcessGate.Capacity);
        await Assert.That(pids.Any(PackOracle.IsProcessAlive)).IsFalse();
    }

    [Test]
    public async Task Slots_WaitTimeout_ThrowsNamedException() {
        for (var i = 0; i < ChildProcessGate.Capacity; i++)
            await ChildProcessGate.AcquireAsync(CancellationToken.None);
        try {
            await Assert.ThrowsAsync<ChildProcessSlotTimeoutException>(
                () => ChildProcessGate.AcquireAsync(CancellationToken.None, TimeSpan.FromMilliseconds(200)));
        }
        finally {
            for (var i = 0; i < ChildProcessGate.Capacity; i++)
                ChildProcessGate.Release();
        }
    }

    [Test]
    public async Task Run_MissingLauncher_ThrowsWin32ErrorAndLosesNoSlot() {
        var spec = new ChildSpec("ignored.dll", []) { Launcher = Path.Combine(Path.GetTempPath(), "no-such-dotnet-" + Guid.NewGuid().ToString("N")) };

        await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(() => PolycProcess.RunAsync(spec));

        for (var i = 0; i < ChildProcessGate.Capacity + 1; i++) {
            await ChildProcessGate.AcquireAsync(CancellationToken.None);
            ChildProcessGate.Release();
        }
    }

    private static ChildSpec HoldSpec(Action<int> started, string holdMs = "5000") {
        var env = new Dictionary<string, string?> {
            ["POLY_CHILD_PROBE"] = "hold",
            ["POLY_CHILD_HOLD_MS"] = holdMs,
            ["POLY_CHILD_DEPTH"] = "1",
        };
        return PolycProcess.TestChildSpec(ProbeFilter(nameof(ChildProbe_Hold)), env) with { Started = started };
    }

    // ───────────────────────────── fixtures: payload for filtered child runs ─────────────────────────────

    private static bool Probing(string name) => Environment.GetEnvironmentVariable("POLY_CHILD_PROBE") == name;

    [Test]
    public async Task ChildProbe_Returns() {
        if (!Probing("return"))
            return;
        Console.Out.WriteLine("probe-returns");
        await Task.CompletedTask;
    }

    [Test]
    public async Task ChildProbe_Fails() {
        if (!Probing("fail"))
            return;
        await Task.CompletedTask;
        throw new InvalidOperationException("probe-fails");
    }

    [Test]
    public async Task ChildProbe_Floods() {
        if (!Probing("flood"))
            return;
        var chunk = new byte[8192];
        Array.Fill(chunk, (byte)'x');
        await using var stdout = Console.OpenStandardOutput();
        await using var stderr = Console.OpenStandardError();
        for (var written = 0; written < 1_500_000; written += chunk.Length) {
            await stdout.WriteAsync(chunk);
            await stderr.WriteAsync(chunk);
        }
    }

    [Test]
    public async Task ChildProbe_Hold() {
        if (!Probing("hold"))
            return;
        var ms = int.TryParse(Environment.GetEnvironmentVariable("POLY_CHILD_HOLD_MS"), out var parsed) ? parsed : 5000;
        await Task.Delay(Math.Clamp(ms, 0, 5000));
    }

    [Test]
    public async Task ChildProbe_OverflowsStack() {
        if (!Probing("overflow-probe") || Environment.GetEnvironmentVariable("POLY_CHILD_DEPTH") != "1")
            return;
        await Task.CompletedTask;
        Console.Out.WriteLine(Overflow(0));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Overflow(int n) {
        Span<byte> pad = stackalloc byte[128];
        pad[0] = (byte)n;
        return Overflow(n + 1) + pad[0];
    }
}