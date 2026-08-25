using Poly.Interpretation;
using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Edge cases for <see cref="VmDebugger"/> and the Normal-mode
/// <c>CompileStatement</c> / <c>DebugHook</c> integration.
/// </summary>
public class VmDebuggerTests {
    [Test]
    public async Task DebugHook_FiresBeforeEachBlockStatement() {
        var nodes = new List<string>();
        var x = new Variable("x");
        var program = Interpreter.Compile(new Block([
            new Assignment(x, new Constant(1L)),
            new Assignment(x, new Constant(2L)),
            x
        ], [x]));
        Interpreter.Execute(program, s => s.DebugHook = (n, _, _) => nodes.Add(n.GetType().Name));
        await Assert.That(nodes).IsEquivalentTo(new[] { "Block", "Assignment", "Assignment", "Variable" });
    }

    [Test]
    public async Task DebugHook_IfNakedAssignment_DoesNotHookInnerStatement() {
        var nodes = new List<string>();
        var x = new Variable("x");
        var program = Interpreter.Compile(new Block([
            new Assignment(x, new Constant(0L)),
            new IfStatement(new Constant(true), new Assignment(x, new Constant(1L))),
            x
        ], [x]));
        Interpreter.Execute(program, s => s.DebugHook = (n, _, _) => nodes.Add(n.GetType().Name));
        await Assert.That(nodes.Count(n => n == "IfStatement")).IsEqualTo(1);
        await Assert.That(nodes.Count(n => n == "Assignment")).IsEqualTo(1);
    }

    [Test]
    public async Task DebugHook_IfBlockThen_HooksInnerStatements() {
        var nodes = new List<string>();
        var x = new Variable("x");
        var program = Interpreter.Compile(new Block([
            new Assignment(x, new Constant(0L)),
            new IfStatement(new Constant(true), new Block([
                new Assignment(x, new Constant(1L))
            ], [x])),
            x
        ], [x]));
        Interpreter.Execute(program, s => s.DebugHook = (n, _, _) => nodes.Add(n.GetType().Name));
        await Assert.That(nodes.Count(n => n == "Assignment")).IsEqualTo(2);
        await Assert.That(nodes.Count(n => n == "Block")).IsEqualTo(1);
    }

    [Test]
    public async Task DebugHook_WhileBlockBody_FiresEachIteration() {
        var assignments = 0;
        var i = new Variable("i");
        var program = Interpreter.Compile(new Block([
            new Assignment(i, new Constant(0L)),
            new WhileLoop(
                new LessThan(i, new Constant(3L)),
                new Block([
                    new Assignment(i, new Add(i, new Constant(1L)))
                ])),
            i
        ], [i]));
        Interpreter.Execute(program, s => s.DebugHook = (n, _, _) => {
            if (n is Assignment) assignments++;
        });
        await Assert.That(assignments).IsEqualTo(4);
    }

    [Test]
    public async Task DebugHook_WhileHeader_FiresOnce() {
        var whiles = 0;
        var i = new Variable("i");
        var program = Interpreter.Compile(new Block([
            new Assignment(i, new Constant(0L)),
            new WhileLoop(
                new LessThan(i, new Constant(2L)),
                new Assignment(i, new Add(i, new Constant(1L)))),
            i
        ], [i]));
        Interpreter.Execute(program, s => s.DebugHook = (n, _, _) => {
            if (n is WhileLoop) whiles++;
        });
        await Assert.That(whiles).IsEqualTo(1);
    }

    [Test]
    public async Task DebugHook_TryCatchFinally_HooksBlockBodies() {
        var kinds = new List<string>();
        var x = new Variable("x");
        var program = Interpreter.Compile(new Block([
            new Assignment(x, new Constant(0L)),
            new TryCatchFinally(
                new Block([
                    new ThrowStatement(new New(TypeReference.To<InvalidOperationException>()))
                ]),
                CatchClauses: [
                    new CatchClause(
                        TypeReference.To<InvalidOperationException>(),
                        null,
                        new Block([new Assignment(x, new Constant(1L))]))
                ],
                FinallyBlock: new Block([new Assignment(x, new Constant(2L))])),
            x
        ], [x]));
        Interpreter.Execute(program, s => s.DebugHook = (n, _, _) => kinds.Add(n.GetType().Name));
        await Assert.That(kinds).Contains("TryCatchFinally");
        await Assert.That(kinds).Contains("ThrowStatement");
        await Assert.That(kinds.Count(k => k == "Assignment")).IsEqualTo(3);
    }

    [Test]
    public async Task DebugHook_LambdaBlockBody_FiresInsideInvoke() {
        var kinds = new List<string>();
        var x = new Parameter("x", TypeReference.To<long>());
        var lambda = new Lambda([x], new Block([new Add(x, new Constant(1L))]));
        var program = Interpreter.Compile(new Invoke(lambda, new Constant(41L)));
        Interpreter.Execute(program, s => s.DebugHook = (n, _, _) => kinds.Add(n.GetType().Name));
        await Assert.That(kinds).Contains("Invoke");
        await Assert.That(kinds).Contains("Add");
    }

    [Test]
    public async Task DebugHook_ReceivesHeapAndCanReadAllocations() {
        Heap? seen = null;
        var program = Interpreter.Compile(new Constant("hi"));
        Interpreter.Execute(program, s => s.DebugHook = (_, _, heap) => seen = heap);
        await Assert.That(seen).IsNotNull();
        await Assert.That(seen!.Get(0)).IsNull();
    }

    [Test]
    public async Task DebugHook_ThrowingHandler_Propagates() {
        var program = Interpreter.Compile(new Constant(1L));
        await Assert.That(() => {
            Interpreter.Execute(program, s => s.DebugHook = (_, _, _) =>
                throw new InvalidOperationException("hook"));
        }).Throws<InvalidOperationException>().WithMessage("hook");
    }

    [Test]
    public async Task DebugInterrupt_IsNotInvokedByEmitter() {
        var fired = false;
        var program = Interpreter.Compile(new Block([
            new Constant(1L),
            new Constant(2L)
        ]));
        Interpreter.Execute(program, s => s.DebugInterrupt = _ => fired = true);
        await Assert.That(fired).IsFalse();
    }

    [Test]
    public async Task DebugHook_NoDebug_DoesNotFireOnBlock() {
        var calls = 0;
        var program = Interpreter.Compile(new Block([
            new Constant(1L),
            new Constant(2L)
        ]), CompilationMode.NoDebug);
        Interpreter.Execute(program, s => s.DebugHook = (_, _, _) => calls++);
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    public async Task VmDebugger_NoDebug_StartCompletesWithoutPause() {
        var program = Interpreter.Compile(new Constant(7L), CompilationMode.NoDebug);
        using var dbg = new VmDebugger(program);
        var result = dbg.Start();
        await Assert.That(result.IsCompleted).IsTrue();
        await Assert.That(dbg.IsCompleted).IsTrue();
    }

    [Test]
    public async Task VmDebugger_Start_PausesAtRootStatement() {
        var program = Interpreter.Compile(new Constant(42L));
        using var dbg = new VmDebugger(program);
        var result = dbg.Start();
        await Assert.That(result.IsCompleted).IsFalse();
        await Assert.That(result.Node).IsTypeOf<Constant>();
        var done = dbg.StepOver();
        await Assert.That(done.IsCompleted).IsTrue();
    }

    [Test]
    public async Task VmDebugger_Continue_RunsToCompletion() {
        var x = new Variable("x");
        var program = Interpreter.Compile(new Block([
            new Assignment(x, new Constant(1L)),
            new Assignment(x, new Constant(2L)),
            x
        ], [x]));
        using var dbg = new VmDebugger(program);
        dbg.Start();
        var result = dbg.Continue();
        await Assert.That(result.IsCompleted).IsTrue();
        await Assert.That(result.Fault).IsNull();
        await Assert.That(dbg.State.Stack.RawSlots[0]).IsEqualTo(2L);
    }

    [Test]
    public async Task VmDebugger_StepOver_AfterComplete_ReturnsCompleted() {
        var program = Interpreter.Compile(new Constant(1L));
        using var dbg = new VmDebugger(program);
        dbg.Start();
        dbg.StepOver();
        var extra = dbg.StepOver();
        await Assert.That(extra.IsCompleted).IsTrue();
    }

    [Test]
    public async Task VmDebugger_StepOver_BeforeStart_Throws() {
        var program = Interpreter.Compile(new Constant(1L));
        using var dbg = new VmDebugger(program);
        await Assert.That(() => dbg.StepOver())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("not been started");
    }

    [Test]
    public async Task VmDebugger_Continue_BeforeStart_Throws() {
        var program = Interpreter.Compile(new Constant(1L));
        using var dbg = new VmDebugger(program);
        await Assert.That(() => dbg.Continue())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("not been started");
    }

    [Test]
    public async Task VmDebugger_Start_Twice_Throws() {
        var program = Interpreter.Compile(new Constant(1L));
        using var dbg = new VmDebugger(program);
        dbg.Start();
        await Assert.That(() => dbg.Start())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("already been started");
        dbg.Continue();
    }

    [Test]
    public async Task VmDebugger_Dispose_UnblocksPausedHook() {
        var program = Interpreter.Compile(new Constant(1L));
        var dbg = new VmDebugger(program);
        dbg.Start();
        dbg.Dispose();
        await Assert.That(dbg.State.DebugHook).IsNull();
    }

    [Test]
    public async Task VmDebugger_Dispose_Twice_IsIdempotent() {
        var program = Interpreter.Compile(new Constant(1L));
        var dbg = new VmDebugger(program);
        dbg.Start();
        dbg.Dispose();
        dbg.Dispose();
        await Assert.That(dbg.State.DebugHook).IsNull();
    }

    [Test]
    public async Task VmDebugger_AfterDispose_StartThrows() {
        var program = Interpreter.Compile(new Constant(1L));
        var dbg = new VmDebugger(program);
        dbg.Dispose();
        await Assert.That(() => dbg.Start()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task VmDebugger_ProgramThrow_IsCapturedAsFault() {
        var program = Interpreter.Compile(
            new ThrowStatement(new New(TypeReference.To<InvalidOperationException>(), new Constant("boom"))));
        using var dbg = new VmDebugger(program);
        var start = dbg.Start();
        DebugResult done = start.IsCompleted ? start : dbg.Continue();
        await Assert.That(done.IsCompleted).IsTrue();
        await Assert.That(done.Fault).IsTypeOf<InvalidOperationException>();
        await Assert.That(dbg.ExecutionException).IsTypeOf<InvalidOperationException>();
        await Assert.That(dbg.ExecutionException!.Message).IsEqualTo("boom");
    }

    [Test]
    public async Task VmDebugger_Start_HonorsCancellation() {
        var i = new Variable("i");
        var program = Interpreter.Compile(new Block([
            new Assignment(i, new Constant(0L)),
            new WhileLoop(new Constant(true), new Assignment(i, new Add(i, new Constant(1L)))),
            i
        ], [i]));
        using var dbg = new VmDebugger(program);
        using var cts = new CancellationTokenSource();
        dbg.Start();
        cts.Cancel();
        await Assert.That(() => dbg.StepOver(cts.Token)).Throws<OperationCanceledException>();
    }

    [Test, Timeout(10_000)]
    public async Task VmDebugger_Continue_HonorsCancellation(CancellationToken ct) {
        var i = new Variable("i");
        var program = Interpreter.Compile(new Block([
            new Assignment(i, new Constant(0L)),
            new WhileLoop(new Constant(true), new Block([
                new Assignment(i, new Add(i, new Constant(1L)))
            ])),
            i
        ], [i]));
        using var dbg = new VmDebugger(program);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        dbg.Start();
        await Assert.That(() => dbg.Continue(cts.Token)).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task VmDebugger_GetLocals_EmptyDebugInfo_IsEmpty() {
        var program = Interpreter.Compile(new Constant(1L));
        var locals = VmDebugger.GetLocals(program, ReadOnlySpan<long>.Empty);
        await Assert.That(locals).IsEmpty();
    }

    [Test]
    public async Task VmDebugger_GetLocals_OffsetPastSpan_IsZero() {
        var layout = new VmDebugInfo([new VariableLayout("x", 5)]);
        var program = Interpreter.Compile(new Constant(1L)) with { DebugInfo = layout };
        var locals = VmDebugger.GetLocals(program, new long[] { 9L }.AsSpan());
        await Assert.That(locals).Count().IsEqualTo(1);
        await Assert.That(locals[0].Name).IsEqualTo("x");
        await Assert.That(locals[0].Value).IsEqualTo(0L);
    }

    [Test]
    public async Task VmDebugger_GetLocals_FromState_AfterExecute() {
        var x = new Variable("x");
        var program = Interpreter.Compile(new Block([
            new Assignment(x, new Constant(11L)),
            x
        ], [x]));
        using var exec = Interpreter.Execute(program);
        var locals = VmDebugger.GetLocals(exec.State);
        var xEntry = locals.FirstOrDefault(l => l.Name == "x");
        await Assert.That(xEntry.Name).IsEqualTo("x");
        await Assert.That(xEntry.Value).IsEqualTo(11L);
    }

    [Test]
    public async Task VmDebugger_FormatCurrentFrame_NoNode_UsesQuestionMark() {
        var program = Interpreter.Compile(new Constant(1L), CompilationMode.NoDebug);
        using var exec = Interpreter.Execute(program);
        var text = VmDebugger.FormatCurrentFrame(exec.State);
        await Assert.That(text).Contains("?");
    }

    [Test]
    public async Task VmDebugger_CurrentAstNode_SetOnlyWhenDebugHookAttached() {
        var program = Interpreter.Compile(new Constant(1L));
        using var without = Interpreter.Execute(program);
        await Assert.That(without.State.CurrentAstNode).IsNull();
        using var withHook = Interpreter.Execute(program, s => s.DebugHook = (_, _, _) => { });
        await Assert.That(withHook.State.CurrentAstNode).IsTypeOf<Constant>();
        var text = VmDebugger.FormatCurrentFrame(withHook.State);
        await Assert.That(text).Contains("Constant");
    }

    [Test]
    public async Task VmDebugger_PreexistingState_UsesSameState() {
        var program = Interpreter.Compile(new Constant(3L));
        var state = new VmState(program) { Registers = new long[256] };
        using var dbg = new VmDebugger(program, state);
        dbg.Start();
        dbg.Continue();
        await Assert.That(dbg.State).IsSameReferenceAs(state);
    }

    [Test]
    public async Task VmDebugger_Return_StopsSteppingAtExit() {
        var x = new Variable("x");
        var program = Interpreter.Compile(new Block([
            new Assignment(x, new Constant(1L)),
            new Return(new Constant(9L)),
            new Assignment(x, new Constant(2L))
        ], [x]));
        using var dbg = new VmDebugger(program);
        dbg.Start();
        DebugResult r;
        do { r = dbg.StepOver(); }
        while (!r.IsCompleted);
        await Assert.That(r.IsCompleted).IsTrue();
        await Assert.That(r.Fault).IsNull();
    }

    [Test]
    public async Task VmDebugger_StepOver_CurrentLocalsMatchesResult() {
        var x = new Variable("x");
        var program = Interpreter.Compile(new Block([
            new Assignment(x, new Constant(4L)),
            x
        ], [x]));
        using var dbg = new VmDebugger(program);
        var start = dbg.Start();
        await Assert.That(dbg.CurrentLocals).IsEquivalentTo(start.Locals);
        var next = dbg.StepOver();
        await Assert.That(dbg.CurrentNode).IsEqualTo(next.Node);
    }

    [Test]
    public async Task VmDebugger_NoDebug_StillHasVariableLayouts() {
        var x = new Variable("x");
        var program = Interpreter.Compile(new Block([
            new Assignment(x, new Constant(1L)),
            x
        ], [x]), CompilationMode.NoDebug);
        var info = program.DebugInfo as VmDebugInfo;
        await Assert.That(info).IsNotNull();
        await Assert.That(info!.Variables.Any(v => v.Name == "x")).IsTrue();
    }

    [Test]
    public async Task VmDebugger_Continue_OnSuspendNode_ReportsSuspend() {
        var program = Interpreter.Compile(new SuspendNode(new Constant(5L), "bp"));
        using var dbg = new VmDebugger(program);
        var start = dbg.Start();
        var r = start.IsSuspend ? start : dbg.Continue();
        await Assert.That(r.IsSuspend || dbg.State.Status == InterpreterStatus.Suspended).IsTrue();
    }
}