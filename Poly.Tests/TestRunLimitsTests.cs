using System.Globalization;

namespace Poly.Tests;

/// <summary>The test host carries durable run settings: a per-test timeout, a GC
/// heap hard limit and the GC mode. This suite froze an 18 GB machine on 2026-11-11,
/// so the limits are asserted here rather than trusted to a shell wrapper. A test
/// that trips either one is a bug to report, never a limit to raise. The GC mode is
/// asserted for the same reason a limit is: it is what keeps a full run's collector
/// cost from being the run's cost.</summary>
public class TestRunLimitsTests {
    private const long FourGiB = 4_294_967_296;

    [Test]
    public async Task TestHost_RuntimeConfig_DeclaresGcHeapHardLimit() {
        var configured = AppContext.GetData("System.GC.HeapHardLimit");

        await Assert.That(configured).IsNotNull();

        var bytes = configured switch {
            long value => value,
            ulong value => (long)value,
            int value => value,
            string text when text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) =>
                Convert.ToInt64(text[2..], 16),
            string text => long.Parse(text, CultureInfo.InvariantCulture),
            _ => Convert.ToInt64(configured, CultureInfo.InvariantCulture),
        };

        await Assert.That(bytes).IsEqualTo(FourGiB);
    }

    /// <summary>This test carries no method- or class-level <c>[Timeout]</c>, so
    /// the value the runner resolved for it is the assembly default. Asserting the
    /// resolved timeout proves the attribute is applied to a real test, not merely
    /// present on the assembly. <c>TestDetails.Timeout</c> is an explicit
    /// implementation of <c>ITestConfiguration.Timeout</c>, hence the
    /// <c>Configuration</c> hop.</summary>
    [Test]
    public async Task TestAssembly_DefaultTimeout_IsAppliedToUnannotatedTest() {
        var current = TestContext.Current;
        await Assert.That(current).IsNotNull();

        var applied = current!.Metadata.TestDetails.Configuration.Timeout;

        await Assert.That(applied as object).IsNotNull();
        await Assert.That(applied!.Value).IsEqualTo(TimeSpan.FromMinutes(3));
    }

    /// <summary>Server GC is a throughput setting, not a limit: the suite allocates about
    /// 660 MB/s across 10k tests, and a workstation collector spent 38% of a run's CPU
    /// collecting. <c>runtimeconfig.template.json</c> sets <c>System.GC.Server</c>, so this
    /// asserts the switch reached the host and cannot regress silently. The host still runs
    /// under the four GiB heap hard limit asserted above; spawned children are unaffected,
    /// because every launcher pins its own <c>DOTNET_gcServer=0</c>.</summary>
    [Test]
    public async Task TestHost_RunsWithServerGc() {
        await Assert.That(System.Runtime.GCSettings.IsServerGC).IsTrue();
    }
}