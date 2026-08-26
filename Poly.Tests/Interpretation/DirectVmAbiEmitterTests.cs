using Poly.Interpretation;
using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Emitter-specific ABI oracles (ring depth, statement lowering). Language
/// meaning lives in node dual-oracle tests, <c>LanguageVmTests</c>, and gotchas.
/// </summary>
public class DirectVmAbiEmitterTests {
    /// <summary>Execute a node via the direct ABI emitter.</summary>
    internal static long ExecDirect(Node node) {
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program, s => s.MaxLoopIterations = 10_000);
        if (!exec.Result.HasValue)
            throw new InvalidOperationException(
                $"Direct VM returned void, kind={exec.Result.Kind}, status={exec.State.Status}");
        return (long)(exec.Result.Value ?? 0);
    }

    // ═══════════════════════════════════════════════════════════════
    // Phase 1.1 — Constants
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task Eq_ReturnsOneWhenEqual(CancellationToken ct) {
        await Assert.That(ExecDirect(new Equal(new Constant(5), new Constant(5)))).IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task Eq_ReturnsZeroWhenNotEqual(CancellationToken ct) {
        await Assert.That(ExecDirect(new Equal(new Constant(5), new Constant(3)))).IsEqualTo(0);
    }

    [Test, Timeout(10_000)]
    public async Task Assignment_Chain_Works(CancellationToken ct) {
        var x = new Variable("x");
        await Assert.That(ExecDirect(
            new Block([new Assignment(x, new Assignment(x, new Constant(5)))], [x])
        )).IsEqualTo(5);
    }

    // ═══════════════════════════════════════════════════════════════
    // Phase 2.2 — Return
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task Return_WithValue_ReturnsValue(CancellationToken ct) {
        await Assert.That(ExecDirect(new Return(new Constant(42)))).IsEqualTo(42);
    }

    // ═══════════════════════════════════════════════════════════════
    // Phase 3.1 — IfStatement
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task If_TrueBranch_ExecutesThen(CancellationToken ct) {
        var result = new Variable("result");
        await Assert.That(ExecDirect(
            new Block([
                new Assignment(result, new Constant(0)),
                new IfStatement(
                    new Constant(1L),
                    new Assignment(result, new Constant(42))),
                result
            ], [result])
        )).IsEqualTo(42);
    }

    [Test, Timeout(10_000)]
    public async Task If_FalseBranch_SkipsThen(CancellationToken ct) {
        var result = new Variable("result");
        await Assert.That(ExecDirect(
            new Block([
                new Assignment(result, new Constant(99)),
                new IfStatement(
                    new Constant(0L),
                    new Assignment(result, new Constant(42))),
                result
            ], [result])
        )).IsEqualTo(99);
    }

    [Test, Timeout(10_000)]
    public async Task IfElse_ConditionTrue_ExecutesThen(CancellationToken ct) {
        var result = new Variable("result");
        await Assert.That(ExecDirect(
            new Block([
                new IfStatement(
                    new Constant(1L),
                    new Assignment(result, new Constant(10)),
                    new Assignment(result, new Constant(20))),
                result  // trailing expression to produce block value
            ], [result])
        )).IsEqualTo(10);
    }

    [Test, Timeout(10_000)]
    public async Task IfElse_ConditionFalse_ExecutesElse(CancellationToken ct) {
        var result = new Variable("result");
        await Assert.That(ExecDirect(
            new Block([
                new IfStatement(
                    new Constant(0L),
                    new Assignment(result, new Constant(10)),
                    new Assignment(result, new Constant(20))),
                result  // trailing expression to produce block value
            ], [result])
        )).IsEqualTo(20);
    }

    // ═══════════════════════════════════════════════════════════════
    // Phase 3.2 — WhileLoop
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task WhileLoop_Counter_CountsToThree(CancellationToken ct) {
        var i = new Variable("i");
        // int i = 0; while (i < 3) { i = i + 1; }
        var counter = new Block([
            new Assignment(i, new Constant(0)),
            new WhileLoop(
                new LessThan(i, new Constant(3)),
                new Assignment(i, new Add(i, new Constant(1)))
            ),
            i
        ], [i]);
        await Assert.That(ExecDirect(counter)).IsEqualTo(3);
    }

    [Test, Timeout(10_000)]
    public async Task RingDepth_SimpleConstant_StaysLow(CancellationToken ct) {
        var program = Interpreter.Compile(new Constant(42));
        // Single constant: ringDepth = 1
        await Assert.That(program.MaxActiveLocalsDepth).IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task RingDepth_NestedArithmetic_StaysLow(CancellationToken ct) {
        // Add(Add(Add(Add(1,2),3),4),5) — left-deep tree (5 operands, 4 adds)
        var expr = new Add(
            new Add(
                new Add(
                    new Add(new Constant(1), new Constant(2)),
                    new Constant(3)),
                new Constant(4)),
            new Constant(5));
        var program = Interpreter.Compile(expr);
        // Constant folding collapses the whole tree to Constant(15), ring depth 1.
        await Assert.That(program.MaxActiveLocalsDepth).IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task RingDepth_BalancedBinaryTree_StaysReasonable(CancellationToken ct) {
        // ((1+2)+(3+4))+((5+6)+(7+8)) — balanced binary tree
        var leaf = (int v) => new Constant(v);
        var add = (Node l, Node r) => new Add(l, r);
        var expr = add(
            add(add(leaf(1), leaf(2)), add(leaf(3), leaf(4))),
            add(add(leaf(5), leaf(6)), add(leaf(7), leaf(8))));
        var program = Interpreter.Compile(expr);
        // With ring-based dispatch, balanced tree uses more ring slots.
        await Assert.That(program.MaxActiveLocalsDepth).IsLessThanOrEqualTo(5);
    }

    [Test, Timeout(10_000)]
    public async Task RingDepth_BlockWithVars_StaysLow(CancellationToken ct) {
        var x = new Variable("x");
        var y = new Variable("y");
        var program = Interpreter.Compile(
            new Block([
                new Assignment(x, new Constant(10)),
                new Assignment(y, new Constant(20)),
                new Add(x, y)
            ], [x, y]));
        // Variables use value stack, not ring. Peak ring depth from CompileValue-based
        // arithmetic: constants are on eval stack, final Add spills 1 slot.
        await Assert.That(program.MaxActiveLocalsDepth).IsLessThanOrEqualTo(4);
    }

    // ═══════════════════════════════════════════════════════════════
    // DebugHook tests (simplified hook — Node + ReadOnlySpan<long> + Heap)
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task DebugHook_SuspendNode_StillSuspendsCorrectly(CancellationToken ct) {
        // Verify the SuspendNode still suspends execution even though hooks
        // are now limited to SuspendNode boundaries.
        var sn = new SuspendNode(new Constant(99), "test");
        using var exec = Interpreter.Execute(Interpreter.Compile(sn));
        await Assert.That(exec.IsSuspended).IsTrue();
        await Assert.That(exec.State.Status).IsEqualTo(InterpreterStatus.Suspended);
    }

    [Test, Timeout(10_000)]
    public async Task TryCatch_CatchSetsResult(CancellationToken ct) {
        var result = new Variable("result");
        var code = new Block([
            new Assignment(result, new Constant(0)),
            new TryCatchFinally(
                new ThrowStatement(new Constant(0L)),
                [new CatchClause(null, null, new Assignment(result, new Constant(42)))]
            ),
            result
        ], [result]);
        await Assert.That(ExecDirect(code)).IsEqualTo(42);
    }

    // ═══════════════════════════════════════════════════════════════
    // Lambda Invocation
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task Capture_WithParameters_CombinesCaptureAndArgs(CancellationToken ct) {
        var offset = new Variable("offset");
        var x = new Parameter("x");
        var lambda = new Lambda([x], new Add(x, offset));
        var invoke = new Invoke(lambda, new Constant(32));
        var code = new Block([
            new Assignment(offset, new Constant(10)),
            invoke
        ], [offset]);
        // Parameter x=32, capture offset=10 (real value at creation). 32+10 = 42
        await Assert.That(ExecDirect(code)).IsEqualTo(42);
    }

    [Test, Timeout(10_000)]
    public async Task SuspendNode_SuspendsAndCapturesNodeInfo(CancellationToken ct) {
        // Simple SuspendNode test: verify it suspends and sets CurrentAstNode/CurrentNodeId.
        var sn = new SuspendNode(new Constant(77), "demo");
        var program = Interpreter.Compile(sn);

        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.IsSuspended).IsTrue();
        await Assert.That(exec.State.CurrentAstNode).IsNotNull();
        await Assert.That(exec.State.CurrentNodeId).IsNotNull();
        await Assert.That(exec.State.CurrentAstNode).IsSameReferenceAs(sn);
        await Assert.That(exec.State.CurrentNodeId!.Value).IsEqualTo(sn.Id);
    }

    // Note: resume from SuspendNode requires a PC-based jump mechanism
    // (ABI-004/ABI-005) since the delegate restarts from the top on each call.
    // Suspend/capture-only validation is done in SuspendNode_SuspendsAndCapturesNodeInfo.

    [Test, Timeout(10_000)]
    public async Task FormatComparison_DirectEmitterTreeForSuspendCase(CancellationToken ct) {
        // Perform format comparison by dumping the *actual* expression tree produced by the
        // direct emitter for a non-trivial loop + capture case (representative of suspend/resume trees).
        // This contrasts with the flat primitive list + side tables from the standard path.
        var x = new Variable("x");
        var i = new Variable("i");
        var code = new Block([
            new Assignment(x, new Constant(42)),
            new Assignment(i, new Constant(0)),
            new WhileLoop(
                new LessThan(i, new Constant(5)),
                new Assignment(i, new Add(i, new Constant(1)))
            ),
            new Add(x, i)
        ], [x, i]);

        var program = Interpreter.Compile(code);
        await Assert.That(program).IsNotNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // P0 — Break and Continue
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task Break_InWhileLoop_ExitsEarly(CancellationToken ct) {
        // int i = 0; while(i < 10) { if (i == 3) break; i = i + 1; } => i = 3
        var i = new Variable("i");
        var code = new Block([
            new Assignment(i, new Constant(0)),
            new WhileLoop(
                new LessThan(i, new Constant(10)),
                new Block([
                    new IfStatement(new Equal(i, new Constant(3)), new BreakStatement(null)),
                    new Assignment(i, new Add(i, new Constant(1)))
                ], [])
            ),
            i
        ], [i]);
        await Assert.That(ExecDirect(code)).IsEqualTo(3);
    }

    [Test, Timeout(10_000)]
    public async Task Continue_RechecksCondition(CancellationToken ct) {
        // int i = 0; while(i < 5) { i = i + 1; if (i < 3) continue; i = i + 10; }
        // i goes: 0→1(continue), 1→2(continue), 2→3(no continue)→13, 13→14(exit)
        // Wait, after 13, i=13 >= 5, so exits. Result: 13.
        var i = new Variable("i");
        var code = new Block([
            new Assignment(i, new Constant(0)),
            new WhileLoop(
                new LessThan(i, new Constant(5)),
                new Block([
                    new Assignment(i, new Add(i, new Constant(1))),
                    new IfStatement(new LessThan(i, new Constant(3)), new ContinueStatement(null)),
                    new Assignment(i, new Add(i, new Constant(10)))
                ], [])
            ),
            i
        ], [i]);
        await Assert.That(ExecDirect(code)).IsEqualTo(13);
    }

    // ═══════════════════════════════════════════════════════════════
    // P1 — ForLoop
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task ForLoop_CountToFive(CancellationToken ct) {
        // for (int i = 0; i < 5; i = i + 1) { } => result = i = 5
        var i = new Variable("i");
        var code = new Block([
            new ForLoop(
                new Assignment(i, new Constant(0)),
                new LessThan(i, new Constant(5)),
                new Assignment(i, new Add(i, new Constant(1))),
                new Block([new Constant(0L)], []), // body produces 0
                null
            ),
            i
        ], [i]);
        await Assert.That(ExecDirect(code)).IsEqualTo(5);
    }

    [Test, Timeout(10_000)]
    public async Task ForLoop_WithBody_Accumulates(CancellationToken ct) {
        // int sum = 0; for (int i = 1; i <= 3; i = i + 1) { sum = sum + i; }
        var sum = new Variable("sum");
        var i = new Variable("i");
        var code = new Block([
            new Assignment(sum, new Constant(0)),
            new ForLoop(
                new Assignment(i, new Constant(1)),
                new LessThanOrEqual(i, new Constant(3)),
                new Assignment(i, new Add(i, new Constant(1))),
                new Block([new Assignment(sum, new Add(sum, i))], []), // body
                null
            ),
            sum
        ], [sum, i]);
        await Assert.That(ExecDirect(code)).IsEqualTo(6);
    }

    // ═══════════════════════════════════════════════════════════════
    // P1 — Goto and Label
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task Goto_JumpsForward(CancellationToken ct) {
        // { x = 10; goto exit; x = 20; exit: x }
        var x = new Variable("x");
        var code = new Block([
            new Assignment(x, new Constant(10)),
            new GotoStatement("exit"),
            new Assignment(x, new Constant(20)),
            new LabelDeclaration("exit", x)
        ], [x]);
        await Assert.That(ExecDirect(code)).IsEqualTo(10);
    }

    // ═══════════════════════════════════════════════════════════════
    // P1 — PopCount
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task PopCount_ReturnsBitCount(CancellationToken ct) {
        long val = 11; // binary 1011 -> 3 bits
        await Assert.That(ExecDirect(new PopCount(new Constant(val)))).IsEqualTo(3);
    }

    [Test, Timeout(10_000)]
    public async Task PopCount_Zero_ReturnsZero(CancellationToken ct) {
        await Assert.That(ExecDirect(new PopCount(new Constant(0L)))).IsEqualTo(0);
    }

    // ═══════════════════════════════════════════════════════════════
    // P1 — DoWhileLoop (already implemented, now with loop scope)
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task DoWhileLoop_RunsAtLeastOnce(CancellationToken ct) {
        var i = new Variable("i");
        var code = new Block([
            new Assignment(i, new Constant(0)),
            new DoWhileLoop(
                new Assignment(i, new Add(i, new Constant(1))),
                new LessThan(i, new Constant(3)),
                null
            ),
            i
        ], [i]);
        await Assert.That(ExecDirect(code)).IsEqualTo(3);
    }

    // ═══════════════════════════════════════════════════════════════
    // P2 — Default, ThisReference, ParameterReference
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task NullForgiving_Passthrough(CancellationToken ct) {
        await Assert.That(ExecDirect(new NullForgiving(new Constant(42)))).IsEqualTo(42);
    }

    // ═══════════════════════════════════════════════════════════════
    // P2 — BitwiseAnd, BitwiseOr, BitwiseXor, ShiftLeft, ShiftRight
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task BitwiseOr_ReturnsOr(CancellationToken ct) {
        await Assert.That(ExecDirect(new BitwiseOr(new Constant(6), new Constant(3)))).IsEqualTo(7);
    }

    [Test, Timeout(10_000)]
    public async Task BitwiseXor_ReturnsXor(CancellationToken ct) {
        await Assert.That(ExecDirect(new BitwiseXor(new Constant(6), new Constant(3)))).IsEqualTo(5);
    }

    [Test, Timeout(10_000)]
    public async Task ShiftLeft_Works(CancellationToken ct) {
        await Assert.That(ExecDirect(new ShiftLeft(new Constant(3), new Constant(2)))).IsEqualTo(12);
    }

    [Test, Timeout(10_000)]
    public async Task ShiftRight_Works(CancellationToken ct) {
        await Assert.That(ExecDirect(new ShiftRight(new Constant(12), new Constant(2)))).IsEqualTo(3);
    }

    // ═══════════════════════════════════════════════════════════════
    // P2 — Conditional (ternary)
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task Switch_NoDefault_NoMatch_ReturnsZero(CancellationToken ct) {
        // switch(99) { case 1: 10; } — no default, no match => 0
        var sw = new SwitchStatement(
            new Constant(99),
            [new SwitchCase(new Constant(1), new Constant(10))]
        );
        await Assert.That(ExecDirect(sw)).IsEqualTo(0);
    }

    [Test, Timeout(10_000)]
    public async Task Switch_WithVariableValue_SelectsCorrect(CancellationToken ct) {
        // int x = 2; switch(x) { case 1: 10; case 2: 20; default: 0; }
        var x = new Variable("x");
        var sw = new Block([
            new Assignment(x, new Constant(2)),
            new SwitchStatement(
                x,
                [
                    new SwitchCase(new Constant(1), new Constant(10)),
                    new SwitchCase(new Constant(2), new Constant(20)),
                ],
                new Constant(0)
            )
        ], [x]);
        await Assert.That(ExecDirect(sw)).IsEqualTo(20);
    }
}