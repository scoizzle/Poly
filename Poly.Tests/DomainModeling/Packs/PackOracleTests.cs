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
}