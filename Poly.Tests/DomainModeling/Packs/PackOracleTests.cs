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
    [Category("OracleFull")]
    public async Task PublicEntityWithInternalManyNav_BootsAndPersistsLines() {
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.PublicOrderInternalLine604);

        using var created = await oracle.Client.PostAsync(
            "/api/orders", new StringContent("""{"Code":"ORD1"}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)created.StatusCode).IsEqualTo(201);

        using var added = await oracle.Client.PostAsync(
            "/api/orders/ORD1/addline", new StringContent("""{"Qty":5}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)added.StatusCode).IsEqualTo(200);

        // OrderLine is a root here (no inverse), so it is listed at the top level rather than under the order.
        // The generated seed row (#595) is also listed, so assert the created line is present, not the count.
        using var lines = await oracle.Client.GetAsync("/api/orderlines");
        await Assert.That(lines.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var linesJson = JsonDocument.Parse(await lines.Content.ReadAsStringAsync());
        await Assert.That(linesJson.RootElement.EnumerateArray().Any(l => l.GetProperty("qty").GetInt32() == 5)).IsTrue();

        // F3 pin: the entity's JSON omits the non-public navigation; the nested route above reads it.
        using var order = await oracle.Client.GetAsync("/api/orders/ORD1");
        await Assert.That(order.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var orderJson = JsonDocument.Parse(await order.Content.ReadAsStringAsync());
        await Assert.That(orderJson.RootElement.TryGetProperty("lines", out _)).IsFalse();
    }

    [Test]
    [Category("OracleFull")]
    public async Task PublicEntityWithInternalOwnedManyNav_ReadsLinesBackThroughNestedRoute() {
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.PublicOrderOwnedInternalLine604);

        using var created = await oracle.Client.PostAsync(
            "/api/orders", new StringContent("""{"Code":"ORD1"}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)created.StatusCode).IsEqualTo(201);

        using var added = await oracle.Client.PostAsync(
            "/api/orders/ORD1/addline", new StringContent("""{"Qty":5}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)added.StatusCode).IsEqualTo(200);

        using var lines = await oracle.Client.GetAsync("/api/orders/ORD1/lines");
        await Assert.That(lines.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var linesJson = JsonDocument.Parse(await lines.Content.ReadAsStringAsync());
        await Assert.That(linesJson.RootElement.GetArrayLength()).IsEqualTo(1);
        await Assert.That(linesJson.RootElement[0].GetProperty("qty").GetInt32()).IsEqualTo(5);
    }

    [Test]
    [Category("OracleFull")]
    public async Task PublicEntityWithInternalUnidirectionalToOneNav_PersistsTheReceipt_DatabaseDoesNotEnforceOneToOne() {
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.PublicOrderInternalReceipt604);

        using var created = await oracle.Client.PostAsync(
            "/api/orders", new StringContent("""{"Code":"ORD1"}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)created.StatusCode).IsEqualTo(201);

        using var issued = await oracle.Client.PostAsync(
            "/api/orders/ORD1/issue", new StringContent("""{"Note":"paid"}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)issued.StatusCode).IsEqualTo(200);

        // A unidirectional to-one target is a root, so it is listed at the top level (with the seed row, #595).
        using var receipts = await oracle.Client.GetAsync("/api/receipts");
        await Assert.That(receipts.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var receiptsJson = JsonDocument.Parse(await receipts.Content.ReadAsStringAsync());
        await Assert.That(receiptsJson.RootElement.EnumerateArray().Any(r => r.GetProperty("note").GetString() == "paid")).IsTrue();

        using var order = await oracle.Client.GetAsync("/api/orders/ORD1");
        using var orderJson = JsonDocument.Parse(await order.Content.ReadAsStringAsync());
        await Assert.That(orderJson.RootElement.TryGetProperty("receipt", out _)).IsFalse();
    }

    [Test]
    [Category("OracleFull")]
    public async Task PublicChildWithInternalParentNav_BootsAndReadsLinesThroughNestedRoute() {
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.PublicLineInternalOrder604);

        using var created = await oracle.Client.PostAsync(
            "/api/orders", new StringContent("""{"Code":"ORD1"}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)created.StatusCode).IsEqualTo(201);

        using var added = await oracle.Client.PostAsync(
            "/api/orders/ORD1/addline", new StringContent("""{"Qty":5}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)added.StatusCode).IsEqualTo(200);

        // No 500 from a serialisation cycle: the line's internal Order navigation is omitted from its JSON.
        using var lines = await oracle.Client.GetAsync("/api/orders/ORD1/lines");
        await Assert.That(lines.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var linesJson = JsonDocument.Parse(await lines.Content.ReadAsStringAsync());
        await Assert.That(linesJson.RootElement.GetArrayLength()).IsEqualTo(1);
        await Assert.That(linesJson.RootElement[0].GetProperty("qty").GetInt32()).IsEqualTo(5);
        await Assert.That(linesJson.RootElement[0].TryGetProperty("order", out _)).IsFalse();
    }

    // F3: two orders keep their own lines apart after reload (a fresh request per read). The no-inverse one-to-many and the
    // unidirectional to-one have no route that loads the navigation (a root target has no nested route), so this pin runs
    // on the aggregate-child shape, where the nested route observes Lines through the generated Collection(...).LoadAsync().
    [Test]
    [Category("OracleFull")]
    public async Task PublicEntityWithInternalOwnedManyNav_KeepsTwoOrdersLinesSeparate() {
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.PublicOrderOwnedInternalLine604);

        foreach (var code in new[] { "ORD1", "ORD2" }) {
            using var created = await oracle.Client.PostAsync(
                "/api/orders", new StringContent($$"""{"Code":"{{code}}"}""", Encoding.UTF8, "application/json"));
            await Assert.That((int)created.StatusCode).IsEqualTo(201);
        }
        foreach (var (code, qty) in new[] { ("ORD1", 5), ("ORD1", 6), ("ORD2", 7) }) {
            using var added = await oracle.Client.PostAsync(
                $"/api/orders/{code}/addline", new StringContent($$"""{"Qty":{{qty}}}""", Encoding.UTF8, "application/json"));
            await Assert.That((int)added.StatusCode).IsEqualTo(200);
        }

        async Task<int[]> QtysAsync(string code) {
            using var lines = await oracle.Client.GetAsync($"/api/orders/{code}/lines");
            using var json = JsonDocument.Parse(await lines.Content.ReadAsStringAsync());
            return json.RootElement.EnumerateArray().Select(l => l.GetProperty("qty").GetInt32()).Order().ToArray();
        }

        await Assert.That(await QtysAsync("ORD1")).IsEquivalentTo(new[] { 5, 6 });
        await Assert.That(await QtysAsync("ORD2")).IsEquivalentTo(new[] { 7 });
    }

    [Test]
    public async Task PublicEntityWithBidirectionalInternalOneToOne_IsPinnedAsNotBooting_Issue791() {
        var fixture = PackOracleDomains.PublicOrderInternalReceiptBidirectional604;

        await Assert.That(fixture.Boots).IsFalse();
        await Assert.That(fixture.NotBootingReason).Contains("#791");
        await Assert.ThrowsAsync<Exception>(async () => {
            await using var oracle = await PackOracle.BootAsync(fixture);
        });
    }

    /// <summary>P-18 (#791) flips this to booting. The harness refuses any Boots:false fixture before compiling, so the real
    /// boot is forced with Boots = true and the child must die: EF cannot determine the dependent side.</summary>
    [Test]
    [Category("OracleFull")]
    public async Task PublicEntityWithBidirectionalInternalOneToOne_BootFails_Issue791() {
        var ex = await Assert.ThrowsAsync<PackOracleBootException>(async () => {
            await using var oracle = await PackOracle.BootAsync(
                PackOracleDomains.PublicOrderInternalReceiptBidirectional604 with { Boots = true });
        });

        await Assert.That(ex!.Message).Contains("dependent side");
    }

    [Test]
    [Category("OracleFull")]
    public async Task PublicEntityWithTwoWayNonOwnedInternalOneToOne_BootFails_Issue791() {
        await Assert.That(PackOracleDomains.PublicOrderInternalReceiptTwoWay604.Boots).IsFalse();
        await Assert.That(PackOracleDomains.PublicOrderInternalReceiptTwoWay604.NotBootingReason).Contains("#791");

        var ex = await Assert.ThrowsAsync<PackOracleBootException>(async () => {
            await using var oracle = await PackOracle.BootAsync(
                PackOracleDomains.PublicOrderInternalReceiptTwoWay604 with { Boots = true });
        });

        await Assert.That(ex!.Message).Contains("dependent side");
    }

    [Test]
    public async Task GeneratedPublicEntityWithPublicNav_BootsAndServesOrderList() {
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.PublicOrderPublicLine604);

        using var list = await oracle.Client.GetAsync("/api/orders");
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.ValueKind).IsEqualTo(JsonValueKind.Array);
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
            await RewritePidFileAsync(oracle.PidFilePath, record);

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
            await RewritePidFileAsync(oracle.PidFilePath, record);

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

    /// <summary>Replaces a pidfile atomically (temp + move): a sweep beside this test can never read a torn
    /// record, which would make it drop the record and leave the run directory behind.</summary>
    private static async Task RewritePidFileAsync(string path, JsonObject record) {
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, record.ToJsonString());
        File.Move(temp, path, overwrite: true);
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

    /// <summary>P-14a: a failure's status comes from its typed kind and every failure body is ProblemDetails.
    /// Every case parses the body and checks status, title (the kind), detail, and the path and guard the kind carries.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task Failure_KindDrivesStatus_AndBodyIsProblemDetails(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.Failures687);

        static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

        static async Task ExpectProblem(HttpResponseMessage response, int status, string title,
            string? detail, string? elementPath, string? guard) {
            await Assert.That((int)response.StatusCode).IsEqualTo(status);
            await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            string? Text(string name) => root.TryGetProperty(name, out var v) ? v.GetString() : null;
            await Assert.That(root.GetProperty("status").GetInt32()).IsEqualTo(status);
            await Assert.That(root.GetProperty("title").GetString()).IsEqualTo(title);
            await Assert.That(Text("detail")).IsEqualTo(detail);
            await Assert.That(Text("elementPath")).IsEqualTo(elementPath);
            await Assert.That(Text("guard")).IsEqualTo(guard);
            await Assert.That(root.TryGetProperty("kind", out _)).IsFalse();
        }

        // Range: 400.
        using var range = await oracle.Client.PostAsync("/api/children", Json("""{"Code":"A1","Qty":500}"""));
        await ExpectProblem(range, 400, "Range", "'Qty' must be <= 100.", "Child.Qty", null);

        // Unique: 409.
        using var first = await oracle.Client.PostAsync("/api/children", Json("""{"Code":"A1","Qty":5}"""));
        await Assert.That((int)first.StatusCode).IsEqualTo(201);
        using var duplicate = await oracle.Client.PostAsync("/api/children", Json("""{"Code":"A1","Qty":5}"""));
        await ExpectProblem(duplicate, 409, "Unique",
            "Unique constraint violated: 'Code' value is already used on another 'Child'.", "Child.Code", null);

        // Link: the action's second create into the now-occupied to-one is 409. Node has no natural
        // key, so its seed row is routed by the shadow id.
        using var relinked = await oracle.Client.PostAsync("/api/nodes/1/extend", Json("{}"));
        await ExpectProblem(relinked, 409, "Link",
            "Cannot add a second link on 'next': the source already has a target.", "Node.next", null);

        // Stage: an action the current stage does not offer is 409 and names the stage.
        using var ticket = await oracle.Client.PostAsync("/api/tickets", Json("""{"Code":"T1","Ok":false}"""));
        await Assert.That((int)ticket.StatusCode).IsEqualTo(201);
        using var cancelled = await oracle.Client.PostAsync("/api/tickets/T1/cancel", Json("{}"));
        await Assert.That((int)cancelled.StatusCode).IsEqualTo(200);
        using var cancelledAgain = await oracle.Client.PostAsync("/api/tickets/T1/cancel", Json("{}"));
        await ExpectProblem(cancelledAgain, 409, "Stage",
            "'Cancel' requires stage 'Draft' on entity 'Ticket'.", "Ticket.Cancel", "Draft");

        // Policy: a require guard that does not hold is 422 and names the policy.
        using var open = await oracle.Client.PostAsync("/api/tickets", Json("""{"Code":"T2","Ok":false}"""));
        await Assert.That((int)open.StatusCode).IsEqualTo(201);
        using var blocked = await oracle.Client.PostAsync("/api/tickets/T2/go", Json("{}"));
        await ExpectProblem(blocked, 422, "Policy", "'Go' blocked by policy 'IsOk'.", "Ticket.Go", "IsOk");

        // Unknown id: 404, no detail.
        using var missing = await oracle.Client.GetAsync("/api/children/ZZZ");
        await ExpectProblem(missing, 404, "Not Found", null, null, null);
    }

    /// <summary>P-13a (#912): the host hands the module the stored link. The first create into an empty <c>next</c>
    /// lands; every later one is the module's own 409 Link and creates no second row. <c>/api/nodes/1</c> is the
    /// seed row (#595, #589), so the count is measured against the count before the first call.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task Action_CreateIntoOccupiedToOne_IsLinkConflict_AndCreatesNoSecondRow_Issue912(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.NodeLink912);

        async Task<int> CountAsync() {
            using var list = JsonDocument.Parse(await oracle.Client.GetStringAsync("/api/nodes"));
            return list.RootElement.GetArrayLength();
        }

        async Task<HttpResponseMessage> ExtendAsync() =>
            await oracle.Client.PostAsync("/api/nodes/1/extend", new StringContent("{}", Encoding.UTF8, "application/json"));

        var seed = await CountAsync();

        using var first = await ExtendAsync();
        await Assert.That((int)first.StatusCode).IsEqualTo(200);
        await Assert.That(await CountAsync()).IsEqualTo(seed + 1);

        foreach (var attempt in new[] { "second", "third" }) {
            using var again = await ExtendAsync();
            await Assert.That((int)again.StatusCode).IsEqualTo(409).Because(attempt);
            await Assert.That(again.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
            using var problem = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
            await Assert.That(problem.RootElement.GetProperty("title").GetString()).IsEqualTo("Link");
            await Assert.That(await CountAsync()).IsEqualTo(seed + 1).Because(attempt);
        }
    }

    /// <summary>P-13a (#658, action half): <c>Triage</c> requires <c>Assigned</c> (<c>assignee exists</c>). The task is
    /// filed in one request and triaged in the next, so the assignee is only known from the stored link.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task Action_GuardOverStoredToOne_SeesTheAssignee_Issue658(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.Desk658);

        static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

        using var user = await oracle.Client.PostAsync("/api/users", Json("""{"Email":"a@x.io"}"""));
        await Assert.That((int)user.StatusCode).IsEqualTo(201);
        using var project = await oracle.Client.PostAsync("/api/projects", Json("""{"Key":"P1"}"""));
        await Assert.That((int)project.StatusCode).IsEqualTo(201);

        using var filed = await oracle.Client.PostAsync("/api/projects/P1/filetask", Json("""{"WhoId":"a@x.io"}"""));
        await Assert.That((int)filed.StatusCode).IsEqualTo(200);

        using var triaged = await oracle.Client.PostAsync("/api/projects/P1/tasks/1/triage", Json("{}"));
        await Assert.That((int)triaged.StatusCode).IsEqualTo(200).Because(await triaged.Content.ReadAsStringAsync());

        using var tasks = JsonDocument.Parse(await oracle.Client.GetStringAsync("/api/projects/P1/tasks"));
        await Assert.That(tasks.RootElement.GetArrayLength()).IsEqualTo(1);
        await Assert.That(tasks.RootElement[0].GetProperty("currentStage").GetString()).IsEqualTo("Done");
    }

    /// <summary>P-13a (#658): a guard through a to-one of a ROOT entity (the shape of crm's Lead.Convert; the guard
    /// reads <c>account</c>, which is not the route parent) answers as the simulator does for the same document
    /// and steps: the status says what the simulator's <c>Invoke</c> outcome says, and a refusal names the same
    /// guard as <c>PolySurfaceHarness.Invoke(...).Result.FailedGuards</c>.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task Action_GuardThroughToOneOfRoot_AnswersAsTheSimulatorDoes_Issue658(CancellationToken cancellationToken) {
        _ = cancellationToken;
        var fixture = PackOracleDomains.LeadConvert658;

        var harness = PolySurfaceHarness.New();
        var workspace = harness.Open("leads");
        await Assert.That(harness.Define(workspace, fixture.Poly).Success).IsTrue();
        var lead = harness.Create(workspace, "Lead", """{"Name":"L1"}""").InstanceId;
        var steps = new[] { "Convert", "Attach", "Convert" };
        var simulated = steps.Select(action => harness.Invoke(workspace, lead, action).Result).ToArray();
        await Assert.That(simulated.Select(r => r.Succeeded).ToArray()).IsEquivalentTo(new[] { false, true, true });
        await Assert.That(simulated[0].FailedGuards.Single()).IsEqualTo("HasAccount");

        await using var oracle = await PackOracle.BootAsync(fixture);
        using var created = await oracle.Client.PostAsync(
            "/api/leads", new StringContent("""{"Name":"L1"}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)created.StatusCode).IsEqualTo(201);

        for (var i = 0; i < steps.Length; i++) {
            using var response = await oracle.Client.PostAsync(
                $"/api/leads/L1/{steps[i].ToLowerInvariant()}", new StringContent("{}", Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();
            await Assert.That(response.IsSuccessStatusCode).IsEqualTo(simulated[i].Succeeded).Because($"step {i} {steps[i]}: {body}");
            if (!simulated[i].Succeeded) {
                using var problem = JsonDocument.Parse(body);
                await Assert.That(problem.RootElement.GetProperty("guard").GetString()).IsEqualTo(simulated[i].FailedGuards.Single());
            }
        }
    }

    /// <summary>#620 (slice P-03): an action that applies an effect and then fails must persist nothing.
    /// The rejected request has already bumped Capacity in memory; a save on the failure branch commits it.
    /// The second call is A3's pin on D2: a failed request must leave no pending change for the next
    /// request's save to flush, so the successful call must see the capacity the failure never saved.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task ActionFailure_DoesNotPersistPartialWrites(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.Warehouse620);

        using var created = await oracle.Client.PostAsync(
            "/api/warehouses",
            new StringContent("""{"Code":"WH2","Capacity":100}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)created.StatusCode).IsEqualTo(201);

        // Used range(0, 100000) rejects 999999 after Capacity has already been incremented.
        using var rejected = await oracle.Client.PostAsync(
            "/api/warehouses/WH2/bad",
            new StringContent("""{"Units":999999}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)rejected.StatusCode).IsEqualTo(400);

        using var snapshot = JsonDocument.Parse(await oracle.Client.GetStringAsync("/api/warehouses/WH2"));
        await Assert.That(snapshot.RootElement.GetProperty("capacity").GetInt64()).IsEqualTo(100);
        await Assert.That(snapshot.RootElement.GetProperty("used").GetInt64()).IsEqualTo(0);

        using var accepted = await oracle.Client.PostAsync(
            "/api/warehouses/WH2/bad",
            new StringContent("""{"Units":100000}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)accepted.StatusCode).IsEqualTo(200);

        using var after = JsonDocument.Parse(await oracle.Client.GetStringAsync("/api/warehouses/WH2"));
        await Assert.That(after.RootElement.GetProperty("capacity").GetInt64()).IsEqualTo(101);
        await Assert.That(after.RootElement.GetProperty("used").GetInt64()).IsEqualTo(100000);
    }

    /// <summary>L-24b: a value type's properties are init-only; an action's value-typed parameter still binds from the
    /// JSON body over HTTP (System.Text.Json writes an init accessor), so the action sees a non-null value.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task ValueTypePayload_BindsOverHttp_WithInitOnlyProperties(CancellationToken cancellationToken) {
        _ = cancellationToken;
        var fixture = new PackOracleDomain(
            "ValueBinding24b",
            """
            domain Billing24b
            uses http
            uses sqlite

            Cost24b: value {
              Amount: Number
              Currency: Text
            }

            Invoice24b: entity {
              Code: Text unique length(3, 8)
              Hits: Number default(0)
              Charge: action (cost: Cost24b) { if (cost != null) { assign Hits to 7 } }
            }
            """,
            "/api/invoice24bs");
        await using var oracle = await PackOracle.BootAsync(fixture);

        using var created = await oracle.Client.PostAsync(
            "/api/invoice24bs",
            new StringContent("""{"Code":"INV1"}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)created.StatusCode).IsEqualTo(201);

        using var charged = await oracle.Client.PostAsync(
            "/api/invoice24bs/INV1/charge",
            new StringContent("""{"Cost":{"Amount":5,"Currency":"USD"}}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)charged.StatusCode).IsEqualTo(200);

        using var after = JsonDocument.Parse(await oracle.Client.GetStringAsync("/api/invoice24bs/INV1"));
        await Assert.That(after.RootElement.GetProperty("hits").GetInt64()).IsEqualTo(7);
    }

    /// <summary>#653 (slice P-03) success-path pin: the save must still run when the action succeeds.
    /// Green on master by construction — it fails only under the over-correction (a handler that never
    /// saves), which the row's red test cannot catch.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task ActionSuccess_PersistsEffects(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.Tally653);

        using var created = await oracle.Client.PostAsync(
            "/api/tallies",
            new StringContent("""{"Name":"t1"}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)created.StatusCode).IsEqualTo(201);

        using var bumped = await oracle.Client.PostAsync(
            "/api/tallies/t1/bump",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        await Assert.That((int)bumped.StatusCode).IsEqualTo(200);

        using var snapshot = JsonDocument.Parse(await oracle.Client.GetStringAsync("/api/tallies/t1"));
        await Assert.That(snapshot.RootElement.GetProperty("n").GetInt64()).IsEqualTo(1);
    }

    /// <summary>#675 (Q13 ruling, 2026-11-12): a designated <c>initial(Open)</c> is honoured on the HTTP
    /// create leg. <c>Draft</c> is declared first, so a row in <c>Draft</c> is the old implicit rule. The
    /// unique <c>Code</c> gives the POST a Location and the created row its own route, so #589's missing
    /// synthetic id and the #595 seed row cannot be mistaken for the row this test made.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task Create_DesignatedInitialStage_HonouredOverHttp(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.Fulfillment675);

        using var created = await oracle.Client.PostAsync(
            "/api/orders",
            new StringContent("""{"Code":"JS-1"}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)created.StatusCode).IsEqualTo(201);

        using var read = await oracle.Client.GetAsync("/api/orders/JS-1");
        await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var row = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        await Assert.That(row.RootElement.GetProperty("currentStage").GetString()).IsEqualTo("Open");
    }

    /// <summary>L-08b half 2 (#683 row L-08): an omitted required value over HTTP is the module's own 400 Required, not
    /// an invented zero. The row's own name: Product.Price is a required Number.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task PostProduct_OmittedRequiredPrice_Returns400Required(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.Inventory588);

        using var omitted = await oracle.Client.PostAsync(
            "/api/products", new StringContent("""{"Sku":"AB1","Name":"Hammer"}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)omitted.StatusCode).IsEqualTo(400);
        await Assert.That(omitted.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        using var problem = JsonDocument.Parse(await omitted.Content.ReadAsStringAsync());
        await Assert.That(problem.RootElement.GetProperty("title").GetString()).IsEqualTo("Required");
        await Assert.That(problem.RootElement.GetProperty("elementPath").GetString()).IsEqualTo("Product.Price");

        using var absentTwin = await oracle.Client.PostAsync(
            "/api/products", new StringContent("""{"Sku":"AB1","Name":"Hammer","Price":null}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)absentTwin.StatusCode).IsEqualTo(400);

        using var supplied = await oracle.Client.PostAsync(
            "/api/products", new StringContent("""{"Sku":"AB1","Name":"Hammer","Price":0}""", Encoding.UTF8, "application/json"));
        await Assert.That((int)supplied.StatusCode).IsEqualTo(201);
    }

    /// <summary>L-08b half 2: for a required Number, Text, Boolean, Date and enum, an absent member and a JSON
    /// <c>null</c> answer exactly what the simulator answers for the same create (Required, the same path and
    /// message); an empty string for the Text is a value on both.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task PostGizmo_OmittedOrNullRequiredValue_AnswersWhatTheSimulatorAnswers(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.RequiredOmitted683);
        var scenario = ParityScenario.FromDsl(PackOracleDomains.RequiredOmittedSimulatorDsl, "PostGizmoSimulatorSide");

        var json = new Dictionary<string, JsonNode?> {
            ["Label"] = "a",
            ["Qty"] = 5,
            ["Active"] = true,
            ["Born"] = "2026-01-01",
            ["Tier"] = "Low",
            ["Price"] = 1.5,
        };
        var sim = new Dictionary<string, object?> {
            ["Label"] = "a",
            ["Qty"] = 5L,
            ["Active"] = true,
            ["Born"] = new DateOnly(2026, 1, 1),
            ["Tier"] = "Low",
            ["Price"] = 1.5m,
        };

        async Task CompareAsync(string name, Func<Dictionary<string, JsonNode?>, Dictionary<string, JsonNode?>> shapeJson,
            Func<Dictionary<string, object?>, Dictionary<string, object?>> shapeSim, bool succeeds) {
            var body = new JsonObject(shapeJson(new(json)).Select(kv => KeyValuePair.Create(kv.Key, kv.Value?.DeepClone())));
            using var response = await oracle.Client.PostAsync(
                "/api/gizmos", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
            var (simulated, _) = scenario.Run(side => side.Create("Gizmo", [.. shapeSim(new(sim)).Select(kv => (kv.Key, kv.Value))]));
            var expected = simulated[0];

            await Assert.That(expected.Success).IsEqualTo(succeeds).Because($"{name}: the simulator's answer");
            if (succeeds) {
                await Assert.That(response.IsSuccessStatusCode).IsTrue().Because($"{name}: {response.StatusCode}");
                return;
            }
            await Assert.That((int)response.StatusCode).IsEqualTo(400).Because(name);
            await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json").Because(name);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await Assert.That(problem.RootElement.GetProperty("title").GetString()).IsEqualTo(expected.FailureKind).Because(name);
            await Assert.That(problem.RootElement.GetProperty("elementPath").GetString()).IsEqualTo(expected.FailurePath).Because(name);
            await Assert.That(problem.RootElement.GetProperty("detail").GetString()).IsEqualTo(expected.Message).Because(name);
        }

        foreach (var member in json.Keys) {
            await CompareAsync($"{member} absent",
                j => { j.Remove(member); return j; }, s => { s.Remove(member); return s; }, succeeds: false);
            await CompareAsync($"{member} null",
                j => { j[member] = null; return j; }, s => { s[member] = null; return s; }, succeeds: false);
        }

        await CompareAsync("Label empty",
            j => { j["Label"] = ""; return j; }, s => { s["Label"] = ""; return s; }, succeeds: true);
    }

    // ── P-13b (ii) (#912): the host loads what an action reads, at any depth ──

    /// <summary>One step of a row, run on the simulator and over HTTP. <c>@Entity:key</c> in either argument string is the
    /// instance id (simulator) or the key (HTTP) of an entity an earlier step made.</summary>
    private sealed record DepthStep(string Entity, string Key, string? Action, string Path, string HttpBody, string? SimArgs, string? Makes = null, string? MakesKey = null);

    private static DepthStep Make(string entity, string key, string path, string properties = "") =>
        new(entity, key, null, path, $$"""{"Code":"{{key}}"{{properties}}}""", $$"""{"Code":"{{key}}"{{properties}}}""");

    /// <summary>An action on a stored instance. <paramref name="makes"/> is the <c>Entity:key</c> of the instance it returns.</summary>
    private static DepthStep Act(string entity, string key, string action, string path, string? makes = null, string http = "{}", string sim = "{}") =>
        new(entity, key, action, path, http, sim, makes?.Split(':')[0], makes?.Split(':')[1]);

    private sealed record DepthRead(string Entity, string Key, string Path, string Property, string? Expected);

    /// <summary>Runs <paramref name="steps"/> on the simulator, then over HTTP, and compares each step's outcome (success, and the
    /// failed guard) and every read back by value. The simulator is the authority; <c>Expected</c> pins it, so a row cannot pass
    /// by both sides being wrong in the same way.</summary>
    private static async Task RunDepthRowAsync(PackOracleDomain fixture, IReadOnlyList<DepthStep> steps, IReadOnlyList<DepthRead> reads) {
        var harness = PolySurfaceHarness.New();
        var workspace = harness.Open("depth");
        var defined = harness.Define(workspace, fixture.Poly);
        await Assert.That(defined.Success).IsTrue().Because(defined.Summary + string.Join("; ", defined.Findings.Select(f => f.ToString())));
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        string Sim(string text) => System.Text.RegularExpressions.Regex.Replace(text, "@(\\w+:\\w+)", m => ids[m.Groups[1].Value]);
        string Http(string text) => System.Text.RegularExpressions.Regex.Replace(text, "@\\w+:(\\w+)", "$1");

        var simulated = new List<InvokeOutcome?>();
        foreach (var step in steps) {
            if (step.Action is null) {
                var made = harness.Create(workspace, step.Entity, Sim(step.SimArgs!));
                await Assert.That(made.Ok).IsTrue().Because($"simulator create {step.Entity}:{step.Key}: {made.Summary}");
                ids[$"{step.Entity}:{step.Key}"] = made.InstanceId;
                simulated.Add(null);
                continue;
            }
            var outcome = harness.Invoke(workspace, ids[$"{step.Entity}:{step.Key}"], step.Action, Sim(step.SimArgs!)).Result;
            simulated.Add(outcome);
            if (step.Makes is not null && outcome.Succeeded)
                ids[$"{step.Makes}:{step.MakesKey}"] = outcome.ReturnInstanceId!;
        }

        var simulatedReads = new List<string?>();
        foreach (var read in reads) {
            if (!ids.ContainsKey($"{read.Entity}:{read.Key}")) {
                var listed = harness.List(workspace, read.Entity).Data;
                var candidates = (listed?["instances"] as System.Text.Json.Nodes.JsonArray)?.Select(i => i?["instanceId"]?.GetValue<string>()).OfType<string>() ?? [];
                ids[$"{read.Entity}:{read.Key}"] = candidates.FirstOrDefault(id =>
                        (harness.Get(workspace, id).Data?["instance"]?["properties"] as System.Text.Json.Nodes.JsonArray)?
                            .Any(p => p?["name"]?.GetValue<string>() == "Code" && p["value"]?.GetValue<string>() == read.Key) == true)
                    ?? throw new InvalidOperationException($"No simulated {read.Entity} with Code {read.Key}: {listed?.ToJsonString()}");
            }
            var snapshot = harness.Get(workspace, ids[$"{read.Entity}:{read.Key}"]);
            var value = (snapshot.Data?["instance"]?["properties"] as System.Text.Json.Nodes.JsonArray)?
                .FirstOrDefault(p => p?["name"]?.GetValue<string>() == read.Property)?["value"]?.GetValue<string>();
            if (value == "(null)")
                value = null;
            simulatedReads.Add(value);
            await Assert.That(value?.ToLowerInvariant()).IsEqualTo(read.Expected?.ToLowerInvariant())
                .Because($"simulator {read.Entity}:{read.Key}.{read.Property}");
        }

        await using var oracle = await PackOracle.BootAsync(fixture);
        for (var i = 0; i < steps.Count; i++) {
            var step = steps[i];
            using var response = await oracle.Client.PostAsync(step.Path, new StringContent(Http(step.HttpBody), Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();
            var label = $"step {i} {step.Entity}:{step.Key} {step.Action ?? "create"} -> {(int)response.StatusCode} {body}";
            if (step.Action is null) {
                await Assert.That((int)response.StatusCode).IsEqualTo(201).Because(label);
                continue;
            }
            await Assert.That(response.IsSuccessStatusCode).IsEqualTo(simulated[i]!.Succeeded).Because(label);
            if (!simulated[i]!.Succeeded && simulated[i]!.FailedGuards.Count > 0) {
                using var problem = JsonDocument.Parse(body);
                await Assert.That(problem.RootElement.TryGetProperty("guard", out var guard) ? guard.GetString() : null).IsEqualTo(simulated[i]!.FailedGuards.Single()).Because(label);
            }
        }

        for (var i = 0; i < reads.Count; i++) {
            var read = reads[i];
            using var row = JsonDocument.Parse(await oracle.Client.GetStringAsync(read.Path));
            var property = char.ToLowerInvariant(read.Property[0]) + read.Property[1..];
            var value = row.RootElement.TryGetProperty(property, out var element) && element.ValueKind != JsonValueKind.Null ? element.ToString() : null;
            await Assert.That(value?.ToLowerInvariant()).IsEqualTo(simulatedReads[i]?.ToLowerInvariant())
                .Because($"HTTP {read.Entity}:{read.Key}.{read.Property}");
        }
    }

    private static readonly DepthStep[] DepthSetup = [
        Make("Order", "o1", "/api/orders"),
        Act("Order", "o1", "MakeCustomer", "/api/orders/o1/makecustomer", makes: "Customer:c1"),
        Act("Customer", "c1", "MakeRegion", "/api/orders/o1/customer/makeregion", makes: "Region:r1"),
    ];

    private static readonly DepthStep[] DepthSetupWithZone = [.. DepthSetup,
        Act("Region", "r1", "MakeZone", "/api/customers/c1/region/makezone", makes: "Zone:z1"),
    ];

    private static IEnumerable<DepthStep> Then(DepthStep[] setup, params DepthStep[] steps) => setup.Concat(steps);

    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task Action_GuardAndReadThroughTwoLinks_AnswersAsTheSimulatorDoes_Issue912(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await RunDepthRowAsync(PackOracleDomains.ReadDepth912,
            [.. Then(DepthSetup, Act("Order", "o1", "TwoHop", "/api/orders/o1/twohop"))],
            [new DepthRead("Order", "o1", "/api/orders/o1", "Note", "n0")]);
    }

    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task Action_ReadThroughThreeLinks_AnswersAsTheSimulatorDoes_Issue912(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await RunDepthRowAsync(PackOracleDomains.ReadDepth912,
            [.. Then(DepthSetupWithZone, Act("Order", "o1", "ThreeHop", "/api/orders/o1/threehop"))],
            [new DepthRead("Order", "o1", "/api/orders/o1", "Note", "zl")]);
    }

    /// <summary>The region is linked and its zone is not: an unlinked link in the middle of a three-link path. Whatever the simulator
    /// answers (success or a failed guard), and whatever it leaves in <c>Note</c>, the host answers by value; no exception escapes.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task Action_ReadThroughAnUnlinkedLinkMidPath_AnswersAsTheSimulatorDoes_Issue912(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await RunDepthRowAsync(PackOracleDomains.ReadDepth912,
            [.. Then(DepthSetup, Act("Order", "o1", "ThreeHop", "/api/orders/o1/threehop"))],
            [new DepthRead("Order", "o1", "/api/orders/o1", "Note", null)]);
    }

    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task Action_QuantifierBodyReadingEachItemsLink_AnswersAsTheSimulatorDoes_Issue912(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await RunDepthRowAsync(PackOracleDomains.ReadDepth912,
            [Make("Order", "o1", "/api/orders"),
             Make("Product", "p1", "/api/products", ",\"Name\":\"pn\""),
             Act("Order", "o1", "AddLine", "/api/orders/o1/addline", makes: "Line:l1", http: """{"PId":"@Product:p1"}""", sim: """{"p":"@Product:p1"}"""),
             Act("Order", "o1", "AnyNamed", "/api/orders/o1/anynamed")],
            [new DepthRead("Order", "o1", "/api/orders/o1", "Flag", "true")]);
    }

    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task Action_FanOutIntoItemsThatReadTheirOwnLink_AnswersAsTheSimulatorDoes_Issue912(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await RunDepthRowAsync(PackOracleDomains.ReadDepth912,
            [Make("Order", "o1", "/api/orders"),
             Make("Product", "p1", "/api/products", ",\"Name\":\"pn\""),
             Act("Order", "o1", "AddLine", "/api/orders/o1/addline", makes: "Line:l1", http: """{"PId":"@Product:p1"}""", sim: """{"p":"@Product:p1"}"""),
             Act("Order", "o1", "FanOut", "/api/orders/o1/fanout")],
            [new DepthRead("Line", "l1", "/api/orders/o1/lines/l1", "Seen", "pn")]);
    }

    /// <summary>The handler of a subscriber registered in the same request reads through a link: the add answered 500.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task Action_WhenHandlerRegisteredInTheSameRequest_ReadsThroughALink_AnswersAsTheSimulatorDoes_Issue912(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await RunDepthRowAsync(PackOracleDomains.Watch912,
            [Make("Watcher", "w1", "/api/watchers"),
             Act("Watcher", "w1", "MakeRegion", "/api/watchers/w1/makeregion", makes: "Region:r1"),
             Act("Region", "r1", "MakeZone", "/api/watchers/w1/region/makezone", makes: "Zone:z1"),
             Act("Watcher", "w1", "AddItem", "/api/watchers/w1/additem")],
            [new DepthRead("Region", "r1", "/api/watchers/w1/region", "Name", "zl")]);
    }

    /// <summary>Fix round 2's case: the client passes the order's own product as <c>p</c>, so the line's handler (registered on
    /// <c>p</c>, fired through <c>this.Product</c>) reads one row through two paths.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task Action_ParameterThatIsTheSameRowAsALink_AnswersAsTheSimulatorDoes_Issue912(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await RunDepthRowAsync(PackOracleDomains.SameRow912,
            [Make("Order", "o1", "/api/orders"),
             Act("Order", "o1", "MakeProduct", "/api/orders/o1/makeproduct", makes: "Product:p1"),
             Act("Product", "p1", "MakeZone", "/api/orders/o1/product/makezone", makes: "Zone:z1", http: """{"Code":"zl"}""", sim: """{"code":"zl"}"""),
             Act("Order", "o1", "Go", "/api/orders/o1/go", http: """{"PId":"@Product:p1"}""", sim: """{"p":"@Product:p1"}""")],
            [new DepthRead("Product", "p1", "/api/orders/o1/product", "Name", "zl")]);
    }

    /// <summary>Two parameters of one entity type: each is looked up and loaded under its own name.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task Action_TwoParametersOfOneEntityType_AnswerAsTheSimulatorDoes_Issue912(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await RunDepthRowAsync(PackOracleDomains.TwoParams912,
            [Make("Order", "o1", "/api/orders"),
             Make("Product", "p1", "/api/products", ",\"Name\":\"n1\""),
             Make("Product", "p2", "/api/products", ",\"Name\":\"n2\""),
             Act("Order", "o1", "Both", "/api/orders/o1/both",
                 http: """{"AId":"@Product:p1","BId":"@Product:p2"}""", sim: """{"a":"@Product:p1","b":"@Product:p2"}""")],
            [new DepthRead("Line", "l1", "/api/orders/o1/lines/l1", "Seen", "n1"),
             new DepthRead("Line", "l2", "/api/orders/o1/lines/l2", "Seen", "n2")]);
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(PackOracle oracle, string path, string member, JsonNode? value) {
        var body = new JsonObject { [member] = value }.ToJsonString();
        return await oracle.Client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));
    }

    /// <summary>P-12a (#687 row P-12): a created row's <c>Location</c> carries its natural key as one escaped path
    /// segment, so a key with a space, a non-ASCII letter, <c>?</c>, <c>#</c>, <c>%</c> or a newline answers 201 and its
    /// Location reads the same row back. A key with a <c>/</c> gets the escaped Location too; ASP.NET does not turn
    /// <c>%2F</c> back into <c>/</c> in a route value, so that route cannot be read back (recorded in the PR, no fix
    /// here: the escaped text is still the right Location).</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task NaturalKey_NeedingEscape_LocationIsEscapedAndRoundTrips(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.RouteKeys687);

        foreach (var key in new[] { "a b", "a b@d", "ä", "a?b#c", "a%2Fb", "a\nb", "a/b", "a b/c@d" }) {
            using var created = await PostJsonAsync(oracle, "/api/customers", "Email", key);
            await Assert.That((int)created.StatusCode).IsEqualTo(201).Because($"POST {JsonSerializer.Serialize(key)}");
            var location = created.Headers.Location?.OriginalString;
            await Assert.That(location).IsEqualTo("/api/customers/" + Uri.EscapeDataString(key)).Because(JsonSerializer.Serialize(key));
            if (key.Contains('/'))
                continue;
            using var read = await oracle.Client.GetAsync(new Uri(location!, UriKind.Relative));
            await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.OK).Because($"GET {location}");
            using var row = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
            await Assert.That(row.RootElement.GetProperty("email").GetString()).IsEqualTo(key);
        }
    }

    /// <summary>P-12a regression guard: a key that needs no escaping gets the same <c>Location</c> as before, a Number
    /// key included, and a shadow-key entity's POST is still a 200 with no <c>Location</c>.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task NaturalKey_NeedingNoEscape_LocationIsUnchanged(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.RouteKeys687);

        using var text = await PostJsonAsync(oracle, "/api/customers", "Email", "plain-key_1.x~");
        await Assert.That((int)text.StatusCode).IsEqualTo(201);
        await Assert.That(text.Headers.Location?.OriginalString).IsEqualTo("/api/customers/plain-key_1.x~");

        using var number = await PostJsonAsync(oracle, "/api/tickets", "Seq", 42);
        await Assert.That((int)number.StatusCode).IsEqualTo(201);
        await Assert.That(number.Headers.Location?.OriginalString).IsEqualTo("/api/tickets/42");
        using var readNumber = await oracle.Client.GetAsync("/api/tickets/42");
        await Assert.That(readNumber.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var shadow = await PostJsonAsync(oracle, "/api/notes", "Body", "hello");
        await Assert.That((int)shadow.StatusCode).IsEqualTo(200);
        await Assert.That(shadow.Headers.Location).IsNull();
    }

    /// <summary>P-12a: a Date key's Location is culture-invariant ISO text that the route binds back. Before, it was the
    /// host's culture short date (<c>1/5/2026</c>), whose slashes made it a different route.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task DateKey_LocationIsInvariantAndRoundTrips(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.RouteKeys687);

        using var created = await PostJsonAsync(oracle, "/api/events", "Day", "2026-01-05");
        await Assert.That((int)created.StatusCode).IsEqualTo(201);
        await Assert.That(created.Headers.Location?.OriginalString).IsEqualTo("/api/events/2026-01-05");
        using var read = await oracle.Client.GetAsync(created.Headers.Location);
        await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var row = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        await Assert.That(row.RootElement.GetProperty("day").GetString()).IsEqualTo("2026-01-05");
    }

    /// <summary>P-12a fix round: an optional unique Date key is a <c>DateOnly?</c>, and the generated host compiles with it
    /// (it did not, CS1501, with the unguarded ISO format) and serves. The create path is not exercised: on this host every
    /// create of such an entity answers 409 Unique (and an optional unique Text key kills the host at boot, a
    /// <c>NullReferenceException</c> in the module's <c>Create</c> from the seed), in the module and outside this slice.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task OptionalUniqueDateKey_HostCompilesAndServes(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.OptionalKeys687);

        using var list = await oracle.Client.GetAsync("/api/slots");
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    /// <summary>P-12a: the GET body of a keyed entity carries every declared scalar and the stage, under the member
    /// names and JSON kinds the declaration implies. Names come from the parsed domain through the BCL's
    /// <see cref="JsonNamingPolicy.CamelCase"/>, not from the generator. A subset-and-required check, deliberately not a
    /// single golden body: it does not say whether <c>id</c> or a navigation is present (the missing <c>id</c> is
    /// P-12's, navigations are unstable), so it pins neither.</summary>
    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task GetBody_MembersFollowTheDeclaredContract(CancellationToken cancellationToken) {
        _ = cancellationToken;
        // The same entities without the host libraries (persistence stands in for sqlite), so the plain parser reads them.
        var declaration = PackOracleDomains.RouteKeys687.Poly.Replace("uses http\n", "").Replace("uses sqlite", "uses persistence");
        var domain = EvolvedDomain.FromDsl(declaration).Domain;
        var profile = domain.Types.OfType<Poly.DomainModeling.Ontology.Entity>().Single(e => e.Name == "Profile");
        var expected = profile.Properties.ToDictionary(
            p => JsonNamingPolicy.CamelCase.ConvertName(p.Name),
            p => p.Type.TypeName switch {
                "Number" or "Decimal" => JsonValueKind.Number,
                "Boolean" => JsonValueKind.True,
                _ => JsonValueKind.String,
            });
        var topology = Poly.DomainModeling.Analysis.EffectTopologyPass.Scan(domain);
        var aggregate = Poly.DomainModeling.Analysis.OwnershipAggregatePass.BuildAggregate(domain, null, topology);
        var stageName = new Poly.DomainModeling.Analysis.StorageAnalyzer(domain).Analyze(aggregate, topology)
            .Entities.Single(e => e.Name == "Profile").StagePropertyName;
        expected[JsonNamingPolicy.CamelCase.ConvertName(stageName!)] = JsonValueKind.String;

        await using var oracle = await PackOracle.BootAsync(PackOracleDomains.RouteKeys687);
        var post = new JsonObject {
            ["Handle"] = "h1",
            ["Age"] = 3,
            ["Active"] = true,
            ["Born"] = "2026-01-01",
            ["Tier"] = "High",
            ["Price"] = 1.5,
        };
        using var created = await oracle.Client.PostAsync(
            "/api/profiles", new StringContent(post.ToJsonString(), Encoding.UTF8, "application/json"));
        await Assert.That((int)created.StatusCode).IsEqualTo(201);

        using var read = await oracle.Client.GetAsync("/api/profiles/h1");
        await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        using var listRead = await oracle.Client.GetAsync("/api/profiles");
        await Assert.That(listRead.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var list = JsonDocument.Parse(await listRead.Content.ReadAsStringAsync());
        await Assert.That(list.RootElement.GetArrayLength()).IsGreaterThan(0);
        // A Boolean is True or False by value, one JSON kind.
        static JsonValueKind Kind(JsonElement e) => e.ValueKind == JsonValueKind.False ? JsonValueKind.True : e.ValueKind;
        foreach (var (name, kind) in expected) {
            await Assert.That(body.RootElement.TryGetProperty(name, out var member)).IsTrue().Because($"member {name}");
            await Assert.That(Kind(member)).IsEqualTo(kind).Because($"member {name}");
            foreach (var item in list.RootElement.EnumerateArray()) {
                await Assert.That(item.TryGetProperty(name, out var listed)).IsTrue().Because($"list member {name}");
                await Assert.That(Kind(listed)).IsEqualTo(kind).Because($"list member {name}");
            }
        }
    }
}
