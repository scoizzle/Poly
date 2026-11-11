using System.Globalization;

namespace Poly.Tests;

/// <summary>The test host carries durable run limits: a per-test timeout and a GC
/// heap hard limit. This suite froze an 18 GB machine on 2026-11-11, so the limits
/// are asserted here rather than trusted to a shell wrapper. A test that trips
/// either one is a bug to report, never a limit to raise.</summary>
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
}