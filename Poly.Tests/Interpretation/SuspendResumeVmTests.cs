using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>F3: SuspendNode + ExecutionResult.Resume / Resuming PC dispatch.</summary>
public class SuspendResumeVmTests {
    [Test]
    public async Task Resume_WhenNotSuspended_Throws() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Constant(1L)));
        await Assert.That(exec.IsSuspended).IsFalse();
        await Assert.That(() => { using var _ = exec.Resume(); })
            .Throws<InvalidOperationException>()
            .WithMessageContaining("not suspended");
    }

    [Test]
    public async Task Suspend_ThenResume_FallsThroughToLaterStatements() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(1L)),
            new SuspendNode(new Constant(0L), "mid"),
            new Assignment(x, new Constant(2L)),
            x
        ], [x]);
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.IsSuspended).IsTrue();
        await Assert.That(exec.State.Status).IsEqualTo(InterpreterStatus.Suspended);
        using var resumed = exec.Resume();
        await Assert.That(resumed.IsSuspended).IsFalse();
        await Assert.That(resumed.Result.GetValue<long>()).IsEqualTo(2L);
    }
}
