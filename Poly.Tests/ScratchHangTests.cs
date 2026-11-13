namespace Poly.Tests;

// SCRATCH: proof of Q-15a2 only. Never merged.
public class ScratchHangTests {
    [Test]
    public async Task BlocksItsThreadForever() {
        Thread.Sleep(Timeout.Infinite);
        await Task.CompletedTask;
    }
}
