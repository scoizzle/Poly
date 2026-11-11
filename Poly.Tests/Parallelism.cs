using TUnit.Core;
using TUnit.Core.Interfaces;

[assembly: ParallelLimiter<Poly.Tests.TestParallelLimit>]
[assembly: Timeout(180_000)]

namespace Poly.Tests;

/// <summary>Caps how many tests run at once. The suite's long poles are Roslyn
/// compilations (golden files, printed C#, demo domains); running a dozen of them
/// concurrently peaks the test host at 7-8 GB and pushes a 16-18 GB machine into
/// swap. Four concurrent compilations peak near 2 GB and finish faster.
/// The assembly timeout here and the GC heap cap in runtimeconfig.template.json
/// exist because a hung or leaking test froze an 18 GB machine on 2026-11-11: a
/// test that trips either limit is a bug to report, never a limit to raise.
/// Measured since, the freeze was contention, not one hung test: clean single
/// full runs take 43-53 s at a peak RSS of 4.1-5.95 GB with no swapping. The
/// timeout's limit is that a test blocked synchronously is failed on time but its
/// thread cannot be killed, so the run-level <c>--timeout</c> is the backstop.</summary>
public sealed class TestParallelLimit : IParallelLimit {
    public int Limit => 4;
}