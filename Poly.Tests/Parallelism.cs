using TUnit.Core.Interfaces;

[assembly: ParallelLimiter<Poly.Tests.TestParallelLimit>]

namespace Poly.Tests;

/// <summary>Caps how many tests run at once. The suite's long poles are Roslyn
/// compilations (golden files, printed C#, demo domains); running a dozen of them
/// concurrently peaks the test host at 7-8 GB and pushes a 16-18 GB machine into
/// swap. Four concurrent compilations peak near 2 GB and finish faster.</summary>
public sealed class TestParallelLimit : IParallelLimit {
    public int Limit => 4;
}