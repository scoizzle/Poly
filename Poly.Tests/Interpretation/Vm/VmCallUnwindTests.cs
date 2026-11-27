using Poly.Interpretation;
using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation.Vm;

/// <summary>
/// #1115 on linux/amd64: a nested VM call recurses on the CLR stack, so a failed run unwinds a chain thousands of
/// frames deep. Restoring each frame with a catch-and-rethrow costs thousands of bytes of the unwind per live level,
/// so a depth refusal thrown deep in an invoke chain overflowed the very stack it protects — the process aborted
/// instead of reporting. The entry point restores the frame state once, and the unwind crosses plain frames.
/// </summary>
public class VmCallUnwindTests {
    /// <summary>
    /// A delegate that fails mid-run leaves the state as it found it: the entry point zeroes the frame pointer,
    /// closure and value-stack pointer once, which is what every call frame used to do on its own way out.
    /// </summary>
    [Test]
    public async Task Execute_DelegateThrows_RestoresEntryFrameState() {
        var program = new VmProgram(s => {
            s.Stack.Push(1L);
            s.Stack.Push(2L);
            s.FramePos = 7;
            s.ClosureHandle = 3;
            throw new InvalidOperationException("boom");
        }, 0);
        using var state = new VmState(program, 64);

        Exception? failure = null;
        try {
            Interpreter.Execute(state);
        }
        catch (InvalidOperationException ex) {
            failure = ex;
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message).IsEqualTo("boom");
        await Assert.That(state.Stack.StackPointer).IsEqualTo(0);
        await Assert.That(state.FramePos).IsEqualTo(0);
        await Assert.That(state.ClosureHandle).IsEqualTo(0);
    }
}