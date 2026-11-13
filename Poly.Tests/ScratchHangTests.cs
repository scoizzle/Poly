using System.Diagnostics;

namespace Poly.Tests;

// SCRATCH: proof of Q-15a2 only. Never merged.
public class ScratchHangTests {
    [Test]
    public async Task WaitsForAChildThatNeverExitsAndHoldsTheOutputPipe() {
        // No redirection: the child inherits the host's stdout, which is the step's output pipe.
        using var child = Process.Start(new ProcessStartInfo("sleep", "3600") { UseShellExecute = false })!;
        child.WaitForExit();
        await Task.CompletedTask;
    }
}
