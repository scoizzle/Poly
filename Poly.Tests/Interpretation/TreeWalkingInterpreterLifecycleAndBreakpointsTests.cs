using Poly.Interpretation.TreeWalking;

namespace Poly.Tests.Interpretation;

public class TreeWalkingInterpreterLifecycleAndBreakpointsTests {
    [Test]
    public async Task Evaluate_WhenAlreadyEvaluating_ThrowsInvalidOperationException() {
        var walker = new TreeWalkingInterpreter();
        var ast = new Block([
            new SuspendNode(new Constant(1), "pause"),
            new Constant(2)
        ]);

        var first = walker.Evaluate(ast);
        await Assert.That(first.HasValue).IsTrue();
        await Assert.That(first.Value).IsTypeOf<SuspendedExecution>();

        await Assert.That(() => walker.Evaluate(new Constant(42)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Resume_WhenNoSuspendedState_ThrowsInvalidOperationException() {
        var walker = new TreeWalkingInterpreter();

        await Assert.That(() => walker.Resume())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Resume_AfterCompletedEvaluation_ThrowsNoSuspendedState() {
        var walker = new TreeWalkingInterpreter();
        var completed = walker.Evaluate(new Constant(9));

        await Assert.That(completed.HasValue).IsTrue();
        await Assert.That(completed.Value).IsEqualTo(9);

        var ex = await Assert.That(() => walker.Resume())
            .Throws<InvalidOperationException>();
        await Assert.That(ex!.Message).Contains("No suspended state to resume");
    }

    [Test]
    public async Task Evaluate_AfterDispose_ThrowsObjectDisposedException() {
        var walker = new TreeWalkingInterpreter();
        walker.Dispose();

        await Assert.That(() => walker.Evaluate(new Constant(1)))
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task BreakpointManagement_ClearBreakpoint_DisablesSpecificBreakpoint() {
        var assignment = new Assignment(new Variable("x"), new Constant(10));
        var finalAdd = new Add(new Constant(2), new Constant(3));
        var ast = new Block([
            assignment,
            finalAdd
        ]);

        var walker = new TreeWalkingInterpreter()
            .BreakOn(assignment)
            .BreakOn(finalAdd)
            .ClearBreakpoint(assignment);

        var first = walker.Evaluate(ast);
        await Assert.That(first.HasValue).IsTrue();
        await Assert.That(first.Value).IsTypeOf<SuspendedExecution>();

        var suspended = (SuspendedExecution)first.Value!;
        await Assert.That(suspended.AtNode).IsSameReferenceAs(finalAdd);

        var resumed = walker.Resume();
        await Assert.That(resumed.HasValue).IsTrue();
        await Assert.That(resumed.Value).IsEqualTo(5);
    }

    [Test]
    public async Task BreakpointManagement_ClearBreakpoints_DisablesAllBreakpoints() {
        var finalAdd = new Add(new Constant(4), new Constant(5));

        var walker = new TreeWalkingInterpreter()
            .BreakOn(finalAdd)
            .ClearBreakpoints();

        var result = walker.Evaluate(finalAdd);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(9);
    }

    [Test]
    public async Task BreakpointOnPureIntermediateNode_IsHit() {
        var pureIntermediate = new Add(new Constant(1), new Constant(2));
        var ast = new Block([
            pureIntermediate,
            new Add(new Constant(10), new Constant(5))
        ]);

        var walker = new TreeWalkingInterpreter()
            .BreakOn(pureIntermediate);

        var result = walker.Evaluate(ast);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(15);
        await Assert.That(result.Value).IsNotTypeOf<SuspendedExecution>();
    }
}