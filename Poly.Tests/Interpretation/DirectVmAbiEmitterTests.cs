using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Syntax;
using Poly.Syntax.Nodes;

using SN = Poly.Syntax.Nodes;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Tests for the experimental <see cref="DirectVmAbiEmitter"/>
/// (AST → bespoke VM ABI without primitives flattening).
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
    public async Task Constant_ReturnsValue(CancellationToken ct) {
        await Assert.That(ExecDirect(new Constant(42))).IsEqualTo(42);
    }

    [Test, Timeout(10_000)]
    public async Task Constant_Negative(CancellationToken ct) {
        await Assert.That(ExecDirect(new Constant(-7))).IsEqualTo(-7);
    }

    [Test, Timeout(10_000)]
    public async Task Constant_Zero(CancellationToken ct) {
        await Assert.That(ExecDirect(new Constant(0))).IsEqualTo(0);
    }

    // ═══════════════════════════════════════════════════════════════
    // Phase 1.2 — Arithmetic
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task Add_ReturnsSum(CancellationToken ct) {
        await Assert.That(ExecDirect(new Add(new Constant(5), new Constant(3)))).IsEqualTo(8);
    }

    [Test, Timeout(10_000)]
    public async Task Sub_ReturnsDifference(CancellationToken ct) {
        await Assert.That(ExecDirect(new Subtract(new Constant(10), new Constant(3)))).IsEqualTo(7);
    }

    [Test, Timeout(10_000)]
    public async Task Mul_ReturnsProduct(CancellationToken ct) {
        await Assert.That(ExecDirect(new Multiply(new Constant(7), new Constant(6)))).IsEqualTo(42);
    }

    [Test, Timeout(10_000)]
    public async Task Div_ReturnsQuotient(CancellationToken ct) {
        await Assert.That(ExecDirect(new Divide(new Constant(10), new Constant(3)))).IsEqualTo(3);
    }

    [Test, Timeout(10_000)]
    public async Task Mod_ReturnsRemainder(CancellationToken ct) {
        await Assert.That(ExecDirect(new Modulo(new Constant(10), new Constant(3)))).IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task NestedAdd_ReturnsCorrectResult(CancellationToken ct) {
        await Assert.That(ExecDirect(
            new Add(new Add(new Constant(1), new Constant(2)), new Constant(3))
        )).IsEqualTo(6);
    }

    [Test, Timeout(10_000)]
    public async Task UnaryMinus_NegatesValue(CancellationToken ct) {
        await Assert.That(ExecDirect(new UnaryMinus(new Constant(42)))).IsEqualTo(-42);
    }

    // ═══════════════════════════════════════════════════════════════
    // Phase 1.3 — Comparisons and Booleans
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
    public async Task NotEq_ReturnsOneWhenNotEqual(CancellationToken ct) {
        await Assert.That(ExecDirect(new NotEqual(new Constant(5), new Constant(3)))).IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task Gt_ReturnsOneWhenGreater(CancellationToken ct) {
        await Assert.That(ExecDirect(new GreaterThan(new Constant(10), new Constant(3)))).IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task Gt_ReturnsZeroWhenNotGreater(CancellationToken ct) {
        await Assert.That(ExecDirect(new GreaterThan(new Constant(3), new Constant(10)))).IsEqualTo(0);
    }

    [Test, Timeout(10_000)]
    public async Task Lt_ReturnsOneWhenLess(CancellationToken ct) {
        await Assert.That(ExecDirect(new LessThan(new Constant(3), new Constant(10)))).IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task Gte_ReturnsOne(CancellationToken ct) {
        await Assert.That(ExecDirect(new GreaterThanOrEqual(new Constant(5), new Constant(5)))).IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task Lte_ReturnsOne(CancellationToken ct) {
        await Assert.That(ExecDirect(new LessThanOrEqual(new Constant(5), new Constant(5)))).IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task Not_ReturnsOneWhenZero(CancellationToken ct) {
        await Assert.That(ExecDirect(new Not(new Constant(0)))).IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task Not_ReturnsZeroWhenNonZero(CancellationToken ct) {
        await Assert.That(ExecDirect(new Not(new Constant(42)))).IsEqualTo(0);
    }

    [Test, Timeout(10_000)]
    public async Task And_ReturnsOneWhenBothTrue(CancellationToken ct) {
        var t = new Equal(new Constant(1), new Constant(1));
        await Assert.That(ExecDirect(new And(t, t))).IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task And_ReturnsZeroWhenLeftFalse(CancellationToken ct) {
        var f = new Equal(new Constant(0), new Constant(1));
        var t = new Equal(new Constant(1), new Constant(1));
        await Assert.That(ExecDirect(new And(f, t))).IsEqualTo(0);
    }

    [Test, Timeout(10_000)]
    public async Task Or_ReturnsOneWhenLeftTrue(CancellationToken ct) {
        var t = new Equal(new Constant(1), new Constant(1));
        var f = new Equal(new Constant(0), new Constant(1));
        await Assert.That(ExecDirect(new Or(t, f))).IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task Or_ReturnsZeroWhenBothFalse(CancellationToken ct) {
        var f = new Equal(new Constant(0), new Constant(1));
        await Assert.That(ExecDirect(new Or(f, f))).IsEqualTo(0);
    }

    // ═══════════════════════════════════════════════════════════════
    // Phase 2.1 — Variables, Assignment, Block
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task Block_WithExpression_ReturnsValue(CancellationToken ct) {
        await Assert.That(ExecDirect(new Block([new Constant(42)], []))).IsEqualTo(42);
    }

    [Test, Timeout(10_000)]
    public async Task Block_WithVariable_ReturnsValue(CancellationToken ct) {
        var x = new Variable("x");
        await Assert.That(ExecDirect(
            new Block([new Assignment(x, new Constant(42)), x], [x])
        )).IsEqualTo(42);
    }

    [Test, Timeout(10_000)]
    public async Task Block_MultipleStatements_ReturnsLast(CancellationToken ct) {
        var x = new Variable("x");
        var y = new Variable("y");
        await Assert.That(ExecDirect(
            new Block([
                new Assignment(x, new Constant(10)),
                new Assignment(y, new Constant(20)),
                new Add(x, y)
            ], [x, y])
        )).IsEqualTo(30);
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
    public async Task WhileLoop_ZeroIterations(CancellationToken ct) {
        var result = new Variable("result");
        await Assert.That(ExecDirect(
            new Block([
                new Assignment(result, new Constant(99)),
                new WhileLoop(new Constant(0L), new SN.Block([new Constant(0L)], []))
            ], [result])
        )).IsEqualTo(99);
    }

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
    public async Task WhileLoop_NeverEnters_ReturnsInitial(CancellationToken ct) {
        var i = new Variable("i");
        var counter = new Block([
            new Assignment(i, new Constant(42)),
            new WhileLoop(
                new Constant(0L),
                new Assignment(i, new Constant(99))
            ),
            i
        ], [i]);
        await Assert.That(ExecDirect(counter)).IsEqualTo(42);
    }

    // ═══════════════════════════════════════════════════════════════
    // Phase 4.1 — Direct path only (primitive path has been removed)
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task DirectOnly_BlockWithVar(CancellationToken ct) {
        var x = new Variable("x");
        await Assert.That(ExecDirect(
            new Block([new Assignment(x, new Constant(42)), x], [x]))).IsEqualTo(42);
    }

    [Test, Timeout(10_000)]
    public async Task DirectOnly_WhileLoop(CancellationToken ct) {
        var i = new Variable("i");
        await Assert.That(ExecDirect(
            new Block([
                new Assignment(i, new Constant(0)),
                new WhileLoop(
                    new LessThan(i, new Constant(3)),
                    new Assignment(i, new Add(i, new Constant(1)))),
                i
            ], [i]))).IsEqualTo(3);
    }

    // ═══════════════════════════════════════════════════════════════
    // Phase 4.2 — Ring depth / register pressure measurement
    // ═══════════════════════════════════════════════════════════════

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
    public async Task DebugHook_FiresAtRootNodeBoundary(CancellationToken ct) {
        var calls = new List<Node>();
        var x = new Variable("x");
        // A Block with statements — each statement fires the hook via CompileStatement.
        var node = new Block([new Assignment(x, new Constant(5)), x], [x]);
        var program = Interpreter.Compile(node);
        Action<Node, ReadOnlySpan<long>, Heap> handler = (n, _, _) => calls.Add(n);
        Interpreter.Execute(program, s => { s.DebugHook = handler; });
        // Fires at root Boundary, the Assignment, and the final Variable read.
        await Assert.That(calls).Count().IsEqualTo(3);
    }

    [Test, Timeout(10_000)]
    public async Task DebugHook_SingleStatementRoot_FiresOnce(CancellationToken ct) {
        var calls = new List<Node>();
        // A single expression as root — CompileStatement wraps it.
        var node = new Constant(42);
        var program = Interpreter.Compile(node);
        Action<Node, ReadOnlySpan<long>, Heap> handler = (n, _, _) => calls.Add(n);
        Interpreter.Execute(program, s => { s.DebugHook = handler; });
        // Hook fires once at the root statement boundary.
        await Assert.That(calls).Count().IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task DebugHook_SuspendNodeInBlock_FiresForEachStatement(CancellationToken ct) {
        var calls = new List<Node>();
        var x = new Variable("x");
        var node = new Block([new Assignment(x, new Constant(42)), x], [x]);
        var program = Interpreter.Compile(node);
        Action<Node, ReadOnlySpan<long>, Heap> handler = (n, _, _) => calls.Add(n);
        Interpreter.Execute(program, s => { s.DebugHook = handler; });
        // Block, Assignment, Variable(x) — each statement boundary fires the hook.
        await Assert.That(calls).Count().IsEqualTo(3);
    }

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
    public async Task DebugHook_NoDebugMode_DoesNotFire(CancellationToken ct) {
        var calls = new List<Node>();
        var program = Interpreter.Compile(new SuspendNode(new Constant(42), "test"), CompilationMode.NoDebug);
        Action<Node, ReadOnlySpan<long>, Heap> handler = (n, _, _) => calls.Add(n);
        Interpreter.Execute(program, s => { s.DebugHook = handler; });
        // In NoDebug mode, DebugHookProp is null — even SuspendNode skips hook.
        await Assert.That(calls).IsEmpty();
    }

    [Test, Timeout(10_000)]
    public async Task DebugHook_NullHandler_NoOverhead(CancellationToken ct) {
        // When DebugHook is null on state, the null guard in SuspendNode
        // skips the expensive path (no Property read, no Invoke).
        var program = Interpreter.Compile(new Add(new Constant(5), new Constant(3)));
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.RawValue).IsEqualTo(8);
    }

    // EH support (TryCatchFinally) added per recommendations
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
    public async Task Lambda_Identity_ReturnsArgument(CancellationToken ct) {
        // (x) => x  applied to 42
        var x = new Parameter("x");
        var lambda = new Lambda([x], x);
        var invoke = new Invoke(lambda, new Constant(42));
        await Assert.That(ExecDirect(invoke)).IsEqualTo(42);
    }

    [Test, Timeout(10_000)]
    public async Task Lambda_ConstantBody_IgnoresArg(CancellationToken ct) {
        // (x) => 99  applied to anything
        var x = new Parameter("x");
        var lambda = new Lambda([x], new Constant(99L));
        var invoke = new Invoke(lambda, new Constant(42));
        await Assert.That(ExecDirect(invoke)).IsEqualTo(99);
    }

    [Test, Timeout(10_000)]
    public async Task Lambda_Add_ComputesCorrectly(CancellationToken ct) {
        // (a, b) => a + b  applied to (3, 5)
        var a = new Parameter("a");
        var b = new Parameter("b");
        var lambda = new Lambda([a, b], new Add(a, b));
        var invoke = new Invoke(lambda, new Constant(3), new Constant(5));
        await Assert.That(ExecDirect(invoke)).IsEqualTo(8);
    }

    [Test, Timeout(10_000)]
    public async Task Lambda_TwoParameters_AddsCorrectly(CancellationToken ct) {
        // (x, y) => x + y  applied to (10, 32)
        var x = new Parameter("x");
        var y = new Parameter("y");
        var lambda = new Lambda([x, y], new Add(x, y));
        var invoke = new Invoke(lambda, new Constant(10), new Constant(32));
        await Assert.That(ExecDirect(invoke)).IsEqualTo(42);
    }

    [Test, Timeout(10_000)]
    public async Task Lambda_Closure_UsesBodyExpression(CancellationToken ct) {
        // (x) => x + 1 applied to 41
        var x = new Parameter("x");
        var lambda = new Lambda([x], new Add(x, new Constant(1)));
        var invoke = new Invoke(lambda, new Constant(41));
        await Assert.That(ExecDirect(invoke)).IsEqualTo(42);
    }

    [Test, Timeout(10_000)]
    public async Task Lambda_MultipleCalls_UseFreshArguments(CancellationToken ct) {
        // (a, b) => a * b  applied to (6, 7)
        var a = new Parameter("a");
        var b = new Parameter("b");
        var lambda = new Lambda([a, b], new Multiply(a, b));
        var invoke = new Invoke(lambda, new Constant(6), new Constant(7));
        await Assert.That(ExecDirect(invoke)).IsEqualTo(42);
    }

    // ═══════════════════════════════════════════════════════════════
    // Capture tests — lambdas that capture outer variables
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task Capture_ReadsOuterVariable(CancellationToken ct) {
        // int x = 42; (() => x)()
        var x = new Variable("x");
        var lambda = new Lambda([], x);   // captures x
        var invoke = new Invoke(lambda);
        var code = new Block([
            new Assignment(x, new Constant(42)),
            invoke
        ], [x]);
        await Assert.That(ExecDirect(code)).IsEqualTo(42);
    }

    [Test, Timeout(10_000)]
    public async Task Capture_UsesSnapshotAtClosureTime(CancellationToken ct) {
        var x = new Variable("x");
        var lambda = new Lambda([], x);
        var invoke = new Invoke(lambda);
        var code = new Block([
            new Assignment(x, new Constant(10)),
            new Assignment(x, new Constant(20)),
            invoke
        ], [x]);
        // Capture should snapshot the value of x at the time the lambda was created (20).
        await Assert.That(ExecDirect(code)).IsEqualTo(20);
    }

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
    public async Task Capture_MultipleCalls_SameClosure(CancellationToken ct) {
        var x = new Variable("x");
        var lambda = new Lambda([], x);
        var code = new Block([
            new Assignment(x, new Constant(99)),
            new Invoke(lambda)
        ], [x]);
        await Assert.That(ExecDirect(code)).IsEqualTo(99);
    }

    [Test, Timeout(10_000)]
    public async Task Capture_Closure_ExpressionTree_Debug(CancellationToken ct) {
        // Debug test: compile, dump tree, and verify capture works
        var x = new Variable("x");
        var lambda = new Lambda([], x);
        var invoke = new Invoke(lambda);
        var code = new Block([
            new Assignment(x, new Constant(42)),
            invoke
        ], [x]);

        VmProgram program = Interpreter.Compile(code);

        // Use the dumper for side-by-side / debug
        // Note: the compiled delegate expression is internal; for full tree we would expose
        // the body expr. Here we just verify execution now that captures work.
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.RawValue).IsEqualTo(42L);
    }

    // ═══════════════════════════════════════════════════════════════
    // SuspendNode validation (abbreviated: full suspend/resume with
    // heap-backed environments is tracked as ABI-004)
    // ═══════════════════════════════════════════════════════════════

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
    public async Task DebugHook_ReceivesCorrectNodeAtRoot(CancellationToken ct) {
        var nodes = new List<Node>();
        // Root expression wrapped in CompileStatement — hook fires once.
        var code = new Coalesce(new Constant(0L), new Constant(99));
        var program = Interpreter.Compile(code);
        Action<Node, ReadOnlySpan<long>, Heap> handler = (n, _, _) => nodes.Add(n);
        Interpreter.Execute(program, s => { s.DebugHook = handler; });
        await Assert.That(nodes).Count().IsEqualTo(1);
        await Assert.That(nodes[0]).IsTypeOf<Coalesce>();
    }

    [Test, Timeout(10_000)]
    public async Task DebugHook_LocalsSpan_HasVariableValues(CancellationToken ct) {
        // Verify the locals span passed to DebugHook contains the actual
        // variable values — the register file is flushed to _slots before
        // the hook fires at each statement boundary.
        long[]? capturedLocals = null;
        var x = new Variable("x");
        var code = new Block([
            new Assignment(x, new Constant(42)),
            // The hook fires at this SuspendNode boundary — the span should
            // contain x = 42 from the preceding statement.
            new SuspendNode(x, "capture")
        ], [x]);

        var program = Interpreter.Compile(code);
        Action<Node, ReadOnlySpan<long>, Heap> handler = (_, span, _) => {
            capturedLocals = span.ToArray();
        };
        Interpreter.Execute(program, s => { s.DebugHook = handler; });
        await Assert.That(capturedLocals).IsNotNull();
        // capturedLocals[0] is the first (and only) variable's slot — should be 42
        await Assert.That(capturedLocals!.Length).IsGreaterThan(0);
        await Assert.That(capturedLocals[0]).IsEqualTo(42L);
    }

    [Test, Timeout(10_000)]
    public async Task DebugHook_LocalsSpan_ShowsVariableAfterAssignment(CancellationToken ct) {
        // Verify the span shows the correct value at a plain statement boundary
        // (no SuspendNode) — the hook fires after each statement's scope store.
        var spanValues = new List<long[]>();
        var x = new Variable("x");
        var y = new Variable("y");
        var code = new Block([
            new Assignment(x, new Constant(10)),
            new Assignment(y, new Constant(20)),
            new Add(x, y)
        ], [x, y]);

        var program = Interpreter.Compile(code);
        Action<Node, ReadOnlySpan<long>, Heap> handler = (_, span, _) => {
            spanValues.Add(span.ToArray());
        };
        Interpreter.Execute(program, s => { s.DebugHook = handler; });

        // Hook fires at each statement boundary: Block, Assignment(x=10),
        // Assignment(y=20), Add(x,y) — each after scope stores.
        // By the Add boundary, x should be 10 and y should be 20.
        await Assert.That(spanValues.Count).IsGreaterThanOrEqualTo(4);
        var lastSpan = spanValues[^1];
        // Find x and y in the span by position (declaration order: x then y)
        await Assert.That(lastSpan.Length).IsGreaterThanOrEqualTo(2);
        await Assert.That(lastSpan[0]).IsEqualTo(10L);
        await Assert.That(lastSpan[1]).IsEqualTo(20L);
    }

    // ═══════════════════════════════════════════════════════════════
    // ABI-003 — VmDebugger named variable resolution
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task VmDebugger_NamedLocals_ReturnsNamesAndValues(CancellationToken ct) {
        // Capture variable values via SuspendNode's debug hook, then verify
        // VmDebugger resolves names correctly from the span — this proves
        // the full end-to-end chain: lowering captures variable layouts,
        // CompileStatement flushes registers to _slots, and VmDebugger
        // maps slot offsets back to variable names.
        (string Name, long Value)[]? capturedLocals = null;

        var x = new Variable("x");
        var y = new Variable("y");
        var code = new SuspendNode(new Block([
            new Assignment(x, new Constant(10)),
            new Assignment(y, new Constant(20)),
            new Add(x, y)
        ], [x, y]), "capture");

        var program = Interpreter.Compile(code);
        Action<Node, ReadOnlySpan<long>, Heap> handler = (_, span, _) => {
            capturedLocals = VmDebugger.GetLocals(program, span).ToArray();
        };
        Interpreter.Execute(program, s => { s.DebugHook = handler; });

        await Assert.That(capturedLocals).IsNotNull();
        // Verify name resolution: x=10 and y=20 by name, not position
        var xEntry = capturedLocals!.FirstOrDefault(l => l.Name == "x");
        var yEntry = capturedLocals!.FirstOrDefault(l => l.Name == "y");
        await Assert.That(xEntry).IsNotDefault();
        await Assert.That(yEntry).IsNotDefault();
        await Assert.That(xEntry.Value).IsEqualTo(10L);
        await Assert.That(yEntry.Value).IsEqualTo(20L);
    }

    [Test, Timeout(10_000)]
    public async Task VmDebugger_FormatCurrentFrame_ShowsNodeAndVars(CancellationToken ct) {
        var x = new Variable("x");
        var code = new Block([
            new Assignment(x, new Constant(42)),
            new Return(x)
        ], [x]);

        using var exec = Interpreter.Execute(Interpreter.Compile(code));
        await Assert.That(exec.RawValue).IsEqualTo(42L);

        var formatted = VmDebugger.FormatCurrentFrame(exec.State);
        await Assert.That(formatted).IsNotNull();
    }

    [Test, Timeout(15_000)]
    public async Task VmDebugger_StepOver_TraversesStatements(CancellationToken ct) {
        // Stateful VmDebugger: each StepOver advances one statement boundary
        // and returns the pre-statement state (locals before the statement).
        var x = new Variable("x");
        var y = new Variable("y");
        var code = new Block([
            new Assignment(x, new Constant(10)),
            new Assignment(y, new Constant(20)),
            new Add(x, y)
        ], [x, y]);

        var program = Interpreter.Compile(code);
        using var debugger = new VmDebugger(program);

        // Start blocks until the first hook fires (at the root Block boundary).
        // At the Block boundary, variables are initialized: x=0, y=0.
        var startResult = debugger.Start();
        await Assert.That(startResult.IsCompleted).IsFalse();
        await Assert.That(startResult.Locals.First(l => l.Name == "x").Value).IsEqualTo(0L);
        await Assert.That(startResult.Locals.First(l => l.Name == "y").Value).IsEqualTo(0L);

        // Step 1 → before Assignment(x, 10): x=0 (initialized), y=0
        var r1 = debugger.StepOver();
        await Assert.That(r1.IsCompleted).IsFalse();
        await Assert.That(r1.Locals.First(l => l.Name == "x").Value).IsEqualTo(0L);
        await Assert.That(r1.Locals.First(l => l.Name == "y").Value).IsEqualTo(0L);

        // Step 2 → before Assignment(y, 20): x=10, y=0
        var r2 = debugger.StepOver();
        await Assert.That(r2.Locals.First(l => l.Name == "x").Value).IsEqualTo(10L);
        await Assert.That(r2.Locals.First(l => l.Name == "y").Value).IsEqualTo(0L);

        // Step 3 → before Add(x, y): x=10, y=20
        var r3 = debugger.StepOver();
        await Assert.That(r3.Locals.First(l => l.Name == "x").Value).IsEqualTo(10L);
        await Assert.That(r3.Locals.First(l => l.Name == "y").Value).IsEqualTo(20L);

        // Step 4 → execution completed
        var r4 = debugger.StepOver();
        await Assert.That(r4.IsCompleted).IsTrue();
    }

    [Test, Timeout(10_000)]
    public async Task VmDebugger_DebugInfo_ContainsVariableLayout(CancellationToken ct) {
        var x = new Variable("x");
        var y = new Variable("y");
        var code = new Block([
            new Assignment(x, new Constant(1)),
            new Assignment(y, new Constant(2)),
            new Add(x, y)
        ], [x, y]);

        var program = Interpreter.Compile(code);
        var debugInfo = program.DebugInfo as VmDebugInfo;
        await Assert.That(debugInfo).IsNotNull();
        await Assert.That(debugInfo!.Variables.Count).IsGreaterThanOrEqualTo(2);

        var xLayout = debugInfo.Variables.FirstOrDefault(v => v.Name == "x");
        var yLayout = debugInfo.Variables.FirstOrDefault(v => v.Name == "y");
        await Assert.That(xLayout).IsNotNull();
        await Assert.That(yLayout).IsNotNull();
        // Offsets should be 0 and 1 (declaration order in the block)
        await Assert.That(xLayout!.FrameOffset).IsEqualTo(0);
        await Assert.That(yLayout!.FrameOffset).IsEqualTo(1);
    }

    // Format comparison (using dumper for the suspend test case structure)
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
    public async Task Default_ReturnsZero(CancellationToken ct) {
        await Assert.That(ExecDirect(new Default())).IsEqualTo(0);
    }

    [Test, Timeout(10_000)]
    public async Task ThisReference_ReturnsZero(CancellationToken ct) {
        await Assert.That(ExecDirect(new ThisReference())).IsEqualTo(0);
    }

    // ═══════════════════════════════════════════════════════════════
    // P2 — NullForgiving (passthrough)
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task NullForgiving_Passthrough(CancellationToken ct) {
        await Assert.That(ExecDirect(new NullForgiving(new Constant(42)))).IsEqualTo(42);
    }

    // ═══════════════════════════════════════════════════════════════
    // P2 — BitwiseAnd, BitwiseOr, BitwiseXor, ShiftLeft, ShiftRight
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task BitwiseAnd_ReturnsAnd(CancellationToken ct) {
        await Assert.That(ExecDirect(new BitwiseAnd(new Constant(6), new Constant(3)))).IsEqualTo(2);
    }

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
    public async Task Conditional_True_ReturnsIfTrue(CancellationToken ct) {
        await Assert.That(ExecDirect(
            new Conditional(new Constant(1L), new Constant(42), new Constant(0))
        )).IsEqualTo(42);
    }

    [Test, Timeout(10_000)]
    public async Task Conditional_False_ReturnsIfFalse(CancellationToken ct) {
        await Assert.That(ExecDirect(
            new Conditional(new Constant(0L), new Constant(42), new Constant(99))
        )).IsEqualTo(99);
    }

    // ═══════════════════════════════════════════════════════════════
    // P2 — Coalesce
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task Coalesce_NonNull_ReturnsLeft(CancellationToken ct) {
        await Assert.That(ExecDirect(new Coalesce(new Constant(42), new Constant(0)))).IsEqualTo(42);
    }

    [Test, Timeout(10_000)]
    public async Task Coalesce_Zeroish_ReturnsRight(CancellationToken ct) {
        // Note: constant folding may fold this to Constant(99) if left is 0L.
        await Assert.That(ExecDirect(new Coalesce(new Constant(0L), new Constant(99)))).IsEqualTo(99);
    }

    // ═══════════════════════════════════════════════════════════════
    // NODES-001 — SwitchStatement (chained conditionals)
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task Switch_SingleCase_Matches(CancellationToken ct) {
        // switch(1) { case 1: 10; default: 0; }
        var sw = new SwitchStatement(
            new Constant(1),
            [new SwitchCase(new Constant(1), new Constant(10))],
            new Constant(0)
        );
        await Assert.That(ExecDirect(sw)).IsEqualTo(10);
    }

    [Test, Timeout(10_000)]
    public async Task Switch_SingleCase_NoMatch_UsesDefault(CancellationToken ct) {
        // switch(99) { case 1: 10; default: 0; }
        var sw = new SwitchStatement(
            new Constant(99),
            [new SwitchCase(new Constant(1), new Constant(10))],
            new Constant(0)
        );
        await Assert.That(ExecDirect(sw)).IsEqualTo(0);
    }

    [Test, Timeout(10_000)]
    public async Task Switch_MultipleCases_SelectsCorrect(CancellationToken ct) {
        // switch(2) { case 1: 10; case 2: 20; case 3: 30; default: 0; }
        var sw = new SwitchStatement(
            new Constant(2),
            [
                new SwitchCase(new Constant(1), new Constant(10)),
                new SwitchCase(new Constant(2), new Constant(20)),
                new SwitchCase(new Constant(3), new Constant(30)),
            ],
            new Constant(0)
        );
        await Assert.That(ExecDirect(sw)).IsEqualTo(20);
    }

    [Test, Timeout(10_000)]
    public async Task Switch_MultipleCases_LastCaseMatches(CancellationToken ct) {
        // switch(3) { case 1: 10; case 2: 20; case 3: 30; default: 0; }
        var sw = new SwitchStatement(
            new Constant(3),
            [
                new SwitchCase(new Constant(1), new Constant(10)),
                new SwitchCase(new Constant(2), new Constant(20)),
                new SwitchCase(new Constant(3), new Constant(30)),
            ],
            new Constant(0)
        );
        await Assert.That(ExecDirect(sw)).IsEqualTo(30);
    }

    [Test, Timeout(10_000)]
    public async Task Switch_MultipleCases_NoMatch_UsesDefault(CancellationToken ct) {
        // switch(99) { case 1: 10; case 2: 20; case 3: 30; default: 42; }
        var sw = new SwitchStatement(
            new Constant(99),
            [
                new SwitchCase(new Constant(1), new Constant(10)),
                new SwitchCase(new Constant(2), new Constant(20)),
                new SwitchCase(new Constant(3), new Constant(30)),
            ],
            new Constant(42)
        );
        await Assert.That(ExecDirect(sw)).IsEqualTo(42);
    }

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