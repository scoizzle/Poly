using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// ABI7 S0: the suspension cases of docs/plans/poly-eng/tasks/ABI7.md §3, each asserted
/// at the behaviour the design requires. A suspend is a spill barrier: every live local
/// is stored to its frame word before the exit and reloaded at the resume label, because
/// resume re-enters the method with CLR locals reset.
/// </summary>
public class SuspendStateTests {
    private static async Task AssertSuspendRejected(Node node) {
        var error = Assert.Throws<InvalidOperationException>(() => Interpreter.Compile(node));
        await Assert.That(error.Message).StartsWith("VM compile rejected:");
        await Assert.That(error.Message).Contains("SuspendNode");
    }

    [Test]
    public async Task VariableSetBeforeSuspend_ReadsThatValueAfterResume() {
        var x = new Variable("x");
        var program = Interpreter.Compile(new Block([
            new Assignment(x, new Constant(41L)),
            new SuspendNode(new Constant(0L), "mid"),
            new Add(x, new Constant(1L)),
        ], [x]));

        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.IsSuspended).IsTrue();
        using var resumed = exec.Resume();
        await Assert.That(resumed.Result.GetValue<long>()).IsEqualTo(42L);
    }

    [Test]
    public async Task VariableReadOnlyBeforeSuspend_IsNotSpilled() {
        var a = new Variable("a");
        var b = new Variable("b");
        var node = new Block([
            new Assignment(a, new Constant(1L)),
            new Assignment(b, new Constant(2L)),
            new SuspendNode(new Constant(0L), "mid"),
            new Add(b, new Constant(1L)),
        ], [a, b]);

        var built = AbiEmitter.Build(node, Interpreter.Analyze(node), CompilationMode.NoDebug);
        var aOffset = built.DebugInfo.Variables.Single(v => v.Name == "a").FrameOffset;
        var bOffset = built.DebugInfo.Variables.Single(v => v.Name == "b").FrameOffset;

        var text = ExpressionText.Render(built.Expression.Body);
        var beforeExit = text[..text.IndexOf("Goto -> _exit", StringComparison.Ordinal)];

        await Assert.That(Occurrences(beforeExit, $"_slots[(_fp + {aOffset})]")).IsEqualTo(0)
            .Because("a is not read after the suspend, so the barrier has nothing to preserve");
        await Assert.That(Occurrences(beforeExit, $"_slots[(_fp + {bOffset})]")).IsEqualTo(1)
            .Because("b is read after the suspend, so the barrier stores it once");
    }

    private static int Occurrences(string text, string value) {
        int count = 0;
        int at = 0;
        while ((at = text.IndexOf(value, at, StringComparison.Ordinal)) >= 0) {
            count++;
            at += value.Length;
        }
        return count;
    }

    [Test]
    public async Task DoubleVariableAcrossSuspend_KeepsItsValue() {
        var d = new Variable("d");
        var program = Interpreter.Compile(new Block([
            new Assignment(d, new Constant(1.5)),
            new SuspendNode(new Constant(0L), "mid"),
            new Multiply(d, new Constant(2.0)),
        ], [d]));

        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.IsSuspended).IsTrue();
        using var resumed = exec.Resume();
        await Assert.That(resumed.Result.GetValue<double>()).IsEqualTo(3.0);
    }

    [Test]
    public async Task ArrayHandleAcrossSuspend_SurvivesResume() {
        var cols = new Variable("cols");
        var program = Interpreter.Compile(new Block([
            new Assignment(cols, new NewArray(TypeReference.To<long>(), new Constant(4L))),
            new Assignment(new IndexAccess(cols, new Constant(1L)), new Constant(5L)),
            new SuspendNode(new Constant(0L), "mid"),
            new IndexAccess(cols, new Constant(1L)),
        ], [cols]));

        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.IsSuspended).IsTrue();
        using var resumed = exec.Resume();
        await Assert.That(resumed.Result.GetValue<long>()).IsEqualTo(5L);
    }

    /// <summary>A loop carries the accumulator up to the suspend; the loop after it
    /// continues from the carried value.</summary>
    [Test]
    public async Task LoopCarriedVariableAcrossSuspend() {
        var sum = new Variable("sum");
        var i = new Variable("i");
        var before = new ForLoop(
            new Assignment(i, new Constant(0L)),
            new LessThan(i, new Constant(3L)),
            new Assignment(i, new Add(i, new Constant(1L))),
            new Assignment(sum, new Add(sum, new Constant(10L))));
        var after = new ForLoop(
            null,
            new LessThan(i, new Constant(5L)),
            new Assignment(i, new Add(i, new Constant(1L))),
            new Assignment(sum, new Add(sum, i)));

        var program = Interpreter.Compile(new Block([
            new Assignment(sum, new Constant(0L)),
            before,
            new SuspendNode(new Constant(0L), "mid"),
            after,
            sum,
        ], [sum, i]));

        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.IsSuspended).IsTrue();
        using var resumed = exec.Resume();
        await Assert.That(resumed.Result.GetValue<long>()).IsEqualTo(37L);
    }

    /// <summary>The right arm's suspend must resume and yield the right value. It is red at
    /// compile today: the resume label lands inside an expression operand, and the CLR
    /// rejects that ("Control cannot enter an expression"). S2 has to hoist the operand's
    /// suspend to a statement position and keep <c>_coal</c> in the spill set.</summary>
    [Test]
    public async Task CoalesceRightArmSuspend_KeepsTheHelperLocal() {
        var age = new Parameter("age", TypeReference.To<long?>());
        var node = new Coalesce(
            age,
            new Block([
                new SuspendNode(new Constant(0L), "mid"),
                new Constant(41L),
            ]));
        var program = Interpreter.Compile(node);

        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { null }));
        await Assert.That(exec.IsSuspended).IsTrue();
        using var resumed = exec.Resume();
        await Assert.That(resumed.Result.GetValue<long>()).IsEqualTo(41L);
    }

    /// <summary>A lifted shift keeps its left operand in <c>_shift</c> across the right
    /// operand's suspend. Red at compile today: the resume label lands inside an operand,
    /// which the CLR rejects, so the operand's suspend has to be hoisted to a statement.</summary>
    [Test]
    public async Task ShiftOperandSuspend_KeepsTheHelperLocal() {
        var n = new Parameter("n", TypeReference.To<long?>());
        var node = new ShiftLeft(n, new Block([
            new SuspendNode(new Constant(0L), "mid"),
            new Constant(1L),
        ]));
        var program = Interpreter.Compile(node);

        using var exec = Interpreter.Execute(program, s => s.SetArgs(4L));
        await Assert.That(exec.IsSuspended).IsTrue();
        using var resumed = exec.Resume();
        await Assert.That(resumed.Result.GetValue<long>()).IsEqualTo(8L);
    }

    /// <summary>The SetArgs frame is parked at slot 4 so the caller frame and the callee's
    /// callSp are not zero, and the callee is nested one inline frame deep, so only a
    /// restored caller frame lets the outer parameter 1 read its slot. A caller frame that
    /// is neither 0 nor callSp is not constructible: an inline frame's caller frame is the
    /// SP-parked frame, and SP does not move between inline calls, so callerFp == callSp.
    /// Parameter 0 aliases <c>_this</c>, so the test reads parameter 1.</summary>
    [Test]
    public async Task SetArgsBodySuspend_RestoresCallerFramePos() {
        var a = new Parameter("a", TypeReference.To<long>());
        var b = new Parameter("b", TypeReference.To<long>());
        var x = new Parameter("x", TypeReference.To<long>());
        var inner = new Invoke(new Lambda([x], new Block([
            new SuspendNode(new Constant(0L), "mid"),
            x,
        ])));
        var outer = new Invoke(new Lambda([a, b], new Block([inner, b])));
        var program = Interpreter.Compile(outer);

        using var exec = Interpreter.Execute(program, s => {
            s.Stack.SetStackPointer(4);
            s.Stack.RawSlots[0] = 0L;
            s.Stack.RawSlots[4] = 7L;
            s.Stack.RawSlots[5] = 42L;
        });
        await Assert.That(exec.IsSuspended).IsTrue();
        using var resumed = exec.Resume();
        await Assert.That(resumed.Result.GetValue<long>()).IsEqualTo(42L);
    }

    [Test]
    public async Task GetLocalsAtSuspend_ReturnsSuspendedValues() {
        var x = new Variable("x");
        var program = Interpreter.Compile(new Block([
            new Assignment(x, new Constant(41L)),
            new SuspendNode(new Constant(0L), "mid"),
            new Add(x, new Constant(1L)),
        ], [x]));

        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.IsSuspended).IsTrue();
        var locals = VmDebugger.GetLocals(exec.State);
        var xEntry = locals.FirstOrDefault(l => l.Name == "x");
        await Assert.That(xEntry.Name).IsEqualTo("x");
        await Assert.That(xEntry.Value).IsEqualTo(41L);
    }

    [Test]
    public async Task DebuggerContinueToSuspend_ReportsSuspendedLocals() {
        var x = new Variable("x");
        var program = Interpreter.Compile(new Block([
            new Assignment(x, new Constant(41L)),
            new SuspendNode(new Constant(0L), "mid"),
            new Add(x, new Constant(1L)),
        ], [x]));

        using var dbg = new VmDebugger(program);
        var start = dbg.Start();
        var r = start.IsSuspend ? start : dbg.Continue();
        await Assert.That(r.IsSuspend || dbg.State.Status == InterpreterStatus.Suspended).IsTrue();
        var xEntry = r.Locals.FirstOrDefault(l => l.Name == "x");
        await Assert.That(xEntry.Name).IsEqualTo("x");
        await Assert.That(xEntry.Value).IsEqualTo(41L);
    }

    [Test]
    public async Task TryCatchSuspend_IsRejectedWithAPositionedError() {
        var x = new Variable("x");
        await AssertSuspendRejected(new Block([
            new TryCatchFinally(new Block([
                new Assignment(x, new Constant(1L)),
                new SuspendNode(new Constant(0L), "mid"),
                x,
            ])),
            x,
        ], [x]));
    }

    [Test]
    public async Task UsingSuspend_IsRejectedWithAPositionedError() {
        var x = new Variable("x");
        await AssertSuspendRejected(new Block([
            new UsingStatement(
                new Constant(new long[] { 1L }),
                new Block([
                    new SuspendNode(new Constant(0L), "mid"),
                    x,
                ])),
            x,
        ], [x]));
    }

    [Test]
    public async Task ForeachSuspend_IsRejectedWithAPositionedError() {
        var item = new Variable("item");
        await AssertSuspendRejected(new Block([
            new ForEachLoop(
                item,
                new Constant(new long[] { 1L, 2L }),
                new Block([
                    new SuspendNode(new Constant(0L), "mid"),
                    item,
                ])),
            new Constant(0L),
        ]));
    }
}