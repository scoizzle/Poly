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

        // #589: GET of an unknown id. The status is what the generated host does today; it is pinned,
        // not endorsed. Whoever fixes #589 changes this line.
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

    [Test]
    public async Task SecondBoot_FreshDatabase_DoesNotSeeFirstBootsWrites() {
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

            // F3: closing the write end of the child's stdin is what a dead parent looks like to it.
            oracle.CloseChildStdin();

            await Assert.That(await oracle.WaitForChildExitAsync(TimeSpan.FromSeconds(30))).IsTrue();
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
            await Assert.That(await oracle.WaitForChildExitAsync(TimeSpan.FromSeconds(30))).IsTrue();
        }
        finally {
            await oracle.DisposeAsync();
        }
        await Assert.That(PackOracle.IsProcessAlive(oracle.ChildPid)).IsFalse();
    }

    [Test]
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
            await Assert.That(await oracle.WaitForChildExitAsync(TimeSpan.FromSeconds(30))).IsTrue();
            await Assert.That(File.Exists(oracle.PidFilePath)).IsFalse();
            await Assert.That(Directory.Exists(oracle.RunDirectory)).IsFalse();
        }
        finally {
            await oracle.DisposeAsync();
        }
        await Assert.That(PackOracle.IsProcessAlive(oracle.ChildPid)).IsFalse();
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