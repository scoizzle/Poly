using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// The PackOracle harness (theme #687, slice P-01): a fixture compiled exactly as polyc does,
/// Roslyn-emitted, booted as a bounded child process on file SQLite. Boot tests carry
/// <c>OracleSubset</c> (the two PR-tier domains, OQ-10) or <c>OracleFull</c>. The harness's own
/// mechanics (cache, timeout, orphan hook, sweep) carry no oracle category.
/// </summary>
public class PackOracleTests {
    public static IEnumerable<Func<PackOracleDomain>> SubsetFixtures() =>
        PackOracleDomains.Subset.Select<PackOracleDomain, Func<PackOracleDomain>>(d => () => d);

    public static IEnumerable<Func<PackOracleDomain>> FullFixtures() =>
        PackOracleDomains.Full.Select<PackOracleDomain, Func<PackOracleDomain>>(d => () => d);

    [After(Class)]
    public static async Task NoRecordedChildSurvivesTheClass() {
        // Every child this class ever booted, checked by pid (kill -0 semantics), not by a machine-wide pgrep.
        var alive = PackOracle.RecordedChildPids.Where(PackOracle.IsProcessAlive).ToArray();
        await Assert.That(alive).IsEmpty();
    }

    [Test]
    public async Task GeneratedInventoryApp_BootsAndServesOrderList() {
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.Inventory588);

        using var list = await oracle.Client.GetAsync("/api/orders");
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.ValueKind).IsEqualTo(JsonValueKind.Array);

        // Pinned: a 404 for an unknown id is the correct behaviour and does not flip with #589.
        // #589 is the unserialised synthetic Id (and a POST that answers 200 without Location), not this status.
        using var missing = await oracle.Client.GetAsync("/api/orders/999");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GeneratedInventoryApp_SeedRow_Present_Pins595() {
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.Inventory588);

        using var list = await oracle.Client.GetAsync("/api/orders");
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());

        // #595 pin: the generated Program.cs seeds a placeholder row into a fresh database.
        // P-07 removes the seed and flips this assertion to zero.
        await Assert.That(json.RootElement.GetArrayLength()).IsEqualTo(1);
    }

    /// <summary>Two boots: the worst case (2 x 135 s) exceeds the assembly's 180 s default, so this test carries
    /// its own ceiling. TUnit requires a CancellationToken parameter on a method-level [Timeout]; the harness
    /// takes no token of its own.</summary>
    [Test]
    [Timeout(300_000)]
    public async Task SecondBoot_FreshDatabase_DoesNotSeeFirstBootsWrites(CancellationToken cancellationToken) {
        _ = cancellationToken;
        string firstDb;
        await using (var first = await PackOracle.BootAsync(PackOracleDomains.Tally653)) {
            using var post = await first.Client.PostAsync(
                "/api/tallies",
                new StringContent("""{"Name":"t1"}""", Encoding.UTF8, "application/json"));
            await Assert.That((int)post.StatusCode).IsEqualTo(201);
            using var seen = JsonDocument.Parse(await first.Client.GetStringAsync("/api/tallies"));
            await Assert.That(NamesOf(seen)).Contains("t1");
            firstDb = first.DatabasePath!;
        }

        await using var second = await PackOracle.BootAsync(PackOracleDomains.Tally653);
        using var after = JsonDocument.Parse(await second.Client.GetStringAsync("/api/tallies"));
        await Assert.That(NamesOf(after)).DoesNotContain("t1");
        await Assert.That(second.DatabasePath).IsNotEqualTo(firstDb);
    }

    private static List<string> NamesOf(JsonDocument array) =>
        array.RootElement.EnumerateArray()
            .Select(e => e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
            .ToList();

    [Test]
    [Category("OracleSubset")]
    [MethodDataSource(nameof(SubsetFixtures))]
    public async Task Boot_SubsetDomain_ServesJsonList(PackOracleDomain fixture) {
        await AssertServesJsonList(fixture);
    }

    [Test]
    [Category("OracleFull")]
    [MethodDataSource(nameof(FullFixtures))]
    public async Task Boot_FullSetDomain_ServesJsonList(PackOracleDomain fixture) {
        await AssertServesJsonList(fixture);
    }

    private static async Task AssertServesJsonList(PackOracleDomain fixture) {
        await using var oracle = await PackOracle.BootAsync(fixture);
        using var response = await oracle.Client.GetAsync(fixture.ListPath);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.ValueKind).IsEqualTo(JsonValueKind.Array);
    }

    [Test]
    public async Task FixtureCatalog_FullEqualsBootableFixtures_AndSubsetIsTwo() {
        await Assert.That(PackOracleDomains.Subset.Select(d => d.Name))
            .IsEquivalentTo(new[] { "Inventory588", "Warehouse620" });
        await Assert.That(PackOracleDomains.Full.Select(d => d.Name))
            .IsEquivalentTo(PackOracleDomains.All.Where(d => d.Boots).Select(d => d.Name));
        await Assert.That(PackOracleDomains.Subset.All(d => PackOracleDomains.Full.Contains(d))).IsTrue();

        var logistics = PackOracleDomains.Logistics559;
        await Assert.That(logistics.Boots).IsFalse();
        await Assert.That(logistics.NotBootingReason).Contains("#559");
        await Assert.That(PackOracleDomains.Full.Contains(logistics)).IsFalse();
    }

    [Test]
    public async Task Compile_ChangedFixtureText_YieldsDifferentAppDirectory() {
        var baseline = PackOracleDomains.Tally653;
        var changed = baseline with {
            Name = "Tally653Changed",
            Poly = baseline.Poly.Replace("Bump: action", "Label: Text optional length(0, 10)\n  Bump: action", StringComparison.Ordinal),
        };

        var a = await PackOracle.CompileAsync(baseline);
        var again = await PackOracle.CompileAsync(baseline);
        var b = await PackOracle.CompileAsync(changed);

        await Assert.That(again.Directory).IsEqualTo(a.Directory);
        await Assert.That(b.Directory).IsNotEqualTo(a.Directory);
        await Assert.That(File.Exists(Path.Combine(a.Directory, "app.dll"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(a.Directory, "app.runtimeconfig.json"))).IsTrue();
    }

    [Test]
    public async Task Boot_ForcedTimeout_KillsChildAndLeavesNoOrphan() {
        var options = new PackOracleBootOptions { Readiness = TimeSpan.FromSeconds(1.5), StallHook = true };

        var failure = await Assert.ThrowsAsync<PackOracleBootException>(
            async () => await PackOracle.BootAsync(PackOracleDomains.Tally653, options: options));

        await Assert.That(failure!.ChildPid).IsNotNull();
        await Assert.That(PackOracle.IsProcessAlive(failure.ChildPid!.Value)).IsFalse();
        await Assert.That(Directory.Exists(failure.RunDirectory)).IsFalse();
    }

    [Test]
    public async Task Hook_ParentStdinClosed_ChildExits() {
        var options = new PackOracleBootOptions { StallHook = true, WaitForReady = false };
        var oracle = await PackOracle.BootAsync(PackOracleDomains.Tally653, options: options);
        try {
            await Assert.That(PackOracle.IsProcessAlive(oracle.ChildPid)).IsTrue();
            await Assert.That(oracle.ChildExitCode.HasValue).IsFalse();

            // F3: closing the write end of the child's stdin is what a dead parent looks like to it.
            oracle.CloseChildStdin();

            await Assert.That(await oracle.WaitForChildExitAsync(TimeSpan.FromSeconds(30))).IsTrue();
            await AssertKill(oracle);
        }
        finally {
            await oracle.DisposeAsync();
        }
        await Assert.That(PackOracle.IsProcessAlive(oracle.ChildPid)).IsFalse();
    }

    [Test]
    public async Task Hook_Deadline_ChildExits() {
        var options = new PackOracleBootOptions {
            StallHook = true,
            WaitForReady = false,
            Deadline = TimeSpan.FromSeconds(2),
        };
        var oracle = await PackOracle.BootAsync(PackOracleDomains.Tally653, options: options);
        try {
            await Assert.That(PackOracle.IsProcessAlive(oracle.ChildPid)).IsTrue();
            await Assert.That(oracle.ChildExitCode.HasValue).IsFalse();

            await Assert.That(await oracle.WaitForChildExitAsync(TimeSpan.FromSeconds(30))).IsTrue();
            await AssertKill(oracle);
        }
        finally {
            await oracle.DisposeAsync();
        }
        await Assert.That(PackOracle.IsProcessAlive(oracle.ChildPid)).IsFalse();
    }

    /// <summary>The hook ends the child with <c>Process.Kill()</c>: a signal death (137 on Unix), never a
    /// clean exit.</summary>
    private static async Task AssertKill(PackOracle oracle) {
        await Assert.That(oracle.ChildExitCode.HasValue).IsTrue();
        await Assert.That(oracle.ChildExitCode!.Value).IsNotEqualTo(0);
        if (!OperatingSystem.IsWindows())
            await Assert.That(oracle.ChildExitCode!.Value).IsEqualTo(137);
    }

    [Test]
    [NotInParallel("PackOracleSweep")]
    public async Task Sweep_DeadHostPidfile_KillsChildAndRemovesRunDir() {
        var options = new PackOracleBootOptions { StallHook = true, WaitForReady = false };
        var oracle = await PackOracle.BootAsync(PackOracleDomains.Tally653, options: options);
        try {
            await Assert.That(PackOracle.IsProcessAlive(oracle.ChildPid)).IsTrue();
            await Assert.That(File.Exists(oracle.PidFilePath)).IsTrue();
            await Assert.That(Directory.Exists(oracle.RunDirectory)).IsTrue();

            // Make the record say its host is dead: a pid that has certainly exited.
            var deadHost = await ExitedProcessIdAsync();
            var record = JsonNode.Parse(await File.ReadAllTextAsync(oracle.PidFilePath))!.AsObject();
            record["hostPid"] = deadHost;
            await File.WriteAllTextAsync(oracle.PidFilePath, record.ToJsonString());

            var killed = PackOracle.SweepOrphans();

            await Assert.That(killed).IsGreaterThanOrEqualTo(1);
            // The record named this test's child, so this pid must be dead now, not merely some pid.
            await Assert.That(PackOracle.IsProcessAlive(oracle.ChildPid)).IsFalse();
            await Assert.That(await oracle.WaitForChildExitAsync(TimeSpan.FromSeconds(30))).IsTrue();
            await Assert.That(File.Exists(oracle.PidFilePath)).IsFalse();
            await Assert.That(Directory.Exists(oracle.RunDirectory)).IsFalse();
        }
        finally {
            await oracle.DisposeAsync();
        }
        await Assert.That(PackOracle.IsProcessAlive(oracle.ChildPid)).IsFalse();
    }

    /// <summary>A dead host record whose start time is an hour off names a pid that may exist but is not the
    /// process the record describes: the sweep drops the record and must not signal the pid (F4).</summary>
    [Test]
    [NotInParallel("PackOracleSweep")]
    public async Task Sweep_StartTimeMismatch_DoesNotKillAndDropsRecord() {
        var options = new PackOracleBootOptions { StallHook = true, WaitForReady = false };
        var oracle = await PackOracle.BootAsync(PackOracleDomains.Tally653, options: options);
        try {
            await Assert.That(PackOracle.IsProcessAlive(oracle.ChildPid)).IsTrue();

            var deadHost = await ExitedProcessIdAsync();
            var record = JsonNode.Parse(await File.ReadAllTextAsync(oracle.PidFilePath))!.AsObject();
            record["hostPid"] = deadHost;
            record["startedUtc"] = DateTime.UtcNow.AddHours(-1);
            await File.WriteAllTextAsync(oracle.PidFilePath, record.ToJsonString());

            PackOracle.SweepOrphans();

            await Assert.That(PackOracle.IsProcessAlive(oracle.ChildPid)).IsTrue();
            await Assert.That(File.Exists(oracle.PidFilePath)).IsFalse();
            await Assert.That(Directory.Exists(oracle.RunDirectory)).IsFalse();
        }
        finally {
            await oracle.DisposeAsync();
        }
        await Assert.That(PackOracle.IsProcessAlive(oracle.ChildPid)).IsFalse();
    }

    /// <summary>A record whose child pid is a live <c>dotnet</c> process that is not our oracle child (same start
    /// time, same process name) is dropped, never signalled (F4). The stand-in is a <c>polyc mcp</c> host from the
    /// one runner, held open on stdin, so only the command line vetoes the kill.</summary>
    [Test]
    [NotInParallel("PackOracleSweep")]
    public async Task Sweep_NameMismatch_DoesNotKill() {
        if (OperatingSystem.IsWindows())
            return; // the Windows record read has no command line; there the hook is the guard.

        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var keep = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        // The child would outlive its own timeout; cancelling `keep` is how this test ends it, and the cancel
        // runs inside the runner's kill-tree-then-bounded-wait path like every other child.
        var child = PolycProcess.RunAsync(
            new ChildSpec(PolycProcess.HostFiles("polyc").Dll, ["mcp"]) {
                Started = pid => started.TrySetResult(pid),
                Timeout = TimeSpan.FromSeconds(20),
            },
            keep.Token);
        var recordPath = Path.Combine(PackOracle.PidDirectoryForTests, Guid.NewGuid().ToString("N") + ".json");
        try {
            var childPid = await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using var live = Process.GetProcessById(childPid);
            Directory.CreateDirectory(PackOracle.PidDirectoryForTests);
            var record = new JsonObject {
                ["hostPid"] = await ExitedProcessIdAsync(),
                ["childPid"] = childPid,
                // The start time matches, so only the process name and command line can veto the kill.
                ["startedUtc"] = live.StartTime.ToUniversalTime(),
                ["runDir"] = Path.Combine(Path.GetTempPath(), "poly-oracle-nowhere-" + Guid.NewGuid().ToString("N")),
                ["appDir"] = null,
            };
            await File.WriteAllTextAsync(recordPath, record.ToJsonString());

            PackOracle.SweepOrphans();

            await Assert.That(PackOracle.IsProcessAlive(childPid)).IsTrue();
        }
        finally {
            await keep.CancelAsync();
            try {
                await child;
            }
            catch (OperationCanceledException) {
            }
            try {
                File.Delete(recordPath);
            }
            catch (IOException) {
            }
        }
    }

    /// <summary>An unreadable record is an I/O or permission wall, not evidence the record is corrupt: the sweep
    /// skips it and keeps it, because it may still name a live child (A5). Skipped on Windows (no permission
    /// bit to withdraw) and when a privileged user reads it anyway.</summary>
    [Test]
    [NotInParallel("PackOracleSweep")]
    public async Task Sweep_UnreadableRecord_IsSkippedAndSurvives() {
        if (OperatingSystem.IsWindows())
            return;

        Directory.CreateDirectory(PackOracle.PidDirectoryForTests);
        var unreadable = Path.Combine(PackOracle.PidDirectoryForTests, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(unreadable, new JsonObject {
            ["hostPid"] = await ExitedProcessIdAsync(),
            ["childPid"] = int.MaxValue,
            ["startedUtc"] = DateTime.UtcNow,
            ["runDir"] = Path.Combine(Path.GetTempPath(), "poly-oracle-unreadable-" + Guid.NewGuid().ToString("N")),
            ["appDir"] = null,
        }.ToJsonString());
        try {
            File.SetUnixFileMode(unreadable, UnixFileMode.None);
            if (await ReadableAsync(unreadable))
                return; // a privileged user reads through the mode: there is no wall to test.

            PackOracle.SweepOrphans(); // must not throw

            await Assert.That(File.Exists(unreadable)).IsTrue();
        }
        finally {
            try {
                File.SetUnixFileMode(unreadable, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (IOException) {
            }
            try {
                File.Delete(unreadable);
            }
            catch (IOException) {
            }
        }
    }

    private static async Task<bool> ReadableAsync(string path) {
        try {
            await File.ReadAllTextAsync(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return false;
        }
    }

    /// <summary>A corrupt record and a record whose host is an alive process we do not own (pid 1) must leave
    /// the sweep quiet: no throw, no signal, and the corrupt record is dropped (F5).</summary>
    [Test]
    [NotInParallel("PackOracleSweep")]
    public async Task Sweep_CorruptAndForeignRecords_DoNotFaultTheSweep() {
        Directory.CreateDirectory(PackOracle.PidDirectoryForTests);
        var corrupt = Path.Combine(PackOracle.PidDirectoryForTests, Guid.NewGuid().ToString("N") + ".json");
        var foreign = Path.Combine(PackOracle.PidDirectoryForTests, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(corrupt, "{ this is not a pid record");
        await File.WriteAllTextAsync(foreign, new JsonObject {
            ["hostPid"] = 1,
            ["childPid"] = int.MaxValue,
            ["startedUtc"] = DateTime.UtcNow,
            ["runDir"] = Path.Combine(Path.GetTempPath(), "poly-oracle-foreign-" + Guid.NewGuid().ToString("N")),
            ["appDir"] = null,
        }.ToJsonString());
        try {
            if (!OperatingSystem.IsWindows())
                await Assert.That(PackOracle.IsProcessAlive(1)).IsTrue();

            PackOracle.SweepOrphans(); // must not throw

            if (!OperatingSystem.IsWindows()) {
                await Assert.That(PackOracle.IsProcessAlive(1)).IsTrue();
                await Assert.That(File.Exists(foreign)).IsTrue(); // a live host's record is never touched
            }
            await Assert.That(File.Exists(corrupt)).IsFalse();
        }
        finally {
            foreach (var path in new[] { corrupt, foreign }) {
                try {
                    File.Delete(path);
                }
                catch (IOException) {
                }
            }
        }
    }

    /// <summary>A cache hit is only a hit when the directory can boot: a damaged entry is rebuilt, not handed
    /// to a child that cannot start. Serialized because it damages the shared cache entry on purpose; its
    /// ceiling (compile 60 s + boot 135 s) exceeds the assembly default.</summary>
    [Test]
    [NotInParallel]
    [Timeout(300_000)]
    public async Task Boot_CacheHitMissingAppDll_RebuildsAndServes(CancellationToken cancellationToken) {
        _ = cancellationToken;
        var cached = await PackOracle.CompileAsync(PackOracleDomains.Tally653);
        var appDll = Path.Combine(cached.Directory, "app.dll");
        await Assert.That(File.Exists(appDll)).IsTrue();

        var parked = appDll + ".parked";
        File.Move(appDll, parked);
        try {
            await using var oracle = await PackOracle.BootAsync(PackOracleDomains.Tally653);
            using var list = await oracle.Client.GetAsync("/api/tallies");
            await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(File.Exists(appDll)).IsTrue();
        }
        finally {
            if (File.Exists(parked)) {
                if (File.Exists(appDll))
                    File.Delete(parked);
                else
                    File.Move(parked, appDll);
            }
        }
    }

    /// <summary>A <c>Process.Start</c> failure must surface as itself (a missing launcher is a Win32 error),
    /// not as the "no process is associated" error of touching an unstarted Process, and it must not leak a
    /// child slot: three failures in a row still leave room for a normal boot. Three attempts plus a boot
    /// exceed the assembly default, so it carries its own ceiling.</summary>
    [Test]
    [Timeout(300_000)]
    public async Task Boot_StartFailure_ThrowsTheRealErrorAndReleasesItsSlot(CancellationToken cancellationToken) {
        _ = cancellationToken;
        var missing = Path.Combine(
            Path.GetTempPath(), "poly-oracle-missing-" + Guid.NewGuid().ToString("N"), "dotnet");
        await Assert.That(File.Exists(missing)).IsFalse();

        var previous = PackOracle.LauncherPathOverride;
        PackOracle.LauncherPathOverride = missing;
        try {
            for (var attempt = 0; attempt < 3; attempt++) {
                var failure = await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(
                    async () => await PackOracle.BootAsync(PackOracleDomains.Tally653));
                await Assert.That(failure!.Message).DoesNotContain("No process is associated");
            }
        }
        finally {
            PackOracle.LauncherPathOverride = previous;
        }

        // Two leaked slots would leave this boot waiting out the slot budget and throwing TimeoutException.
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.Tally653);
        using var list = await oracle.Client.GetAsync("/api/tallies");
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static async Task<int> ExitedProcessIdAsync() {
        using var process = Process.Start(new ProcessStartInfo(PackOracle.LauncherPath(), "--version") {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        var pid = process.Id;
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return pid;
    }
}