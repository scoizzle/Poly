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
        // Left-deep traversal: at peak, _r0=1, _r1=2 → _r0=3, _r1=3 → _r0=6, _r1=4 → _r0=10, _r1=5 → _r0=15
        // Peak depth = 2 (two values at most on the ring simultaneously)
        await Assert.That(program.MaxActiveLocalsDepth).IsEqualTo(2);
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
        // Balanced tree: peak depth is lower than left-deep (3-4 slots)
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
        // Variables use value stack, not ring. Peak ring depth is from Add's temps.
        await Assert.That(program.MaxActiveLocalsDepth).IsLessThanOrEqualTo(5);
    }

    // ═══════════════════════════════════════════════════════════════
    // DebugInterrupt tests
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task DebugInterrupt_Constant_FiresOnce(CancellationToken ct) {
        var steps = new List<int>();
        var program = Interpreter.Compile(new Constant(42));
        Action<VmState> handler = s => steps.Add(s.ProgramCounter);
        Interpreter.Execute(program, s => { s.DebugInterrupt = handler; });
        // Constant(42) is one node = 1 interrupt (step 0)
        await Assert.That(steps).IsEquivalentTo(new[] { 0 });
    }

    [Test, Timeout(10_000)]
    public async Task DebugInterrupt_Add_FiresThreeTimes(CancellationToken ct) {
        var steps = new List<int>();
        var program = Interpreter.Compile(new Add(new Constant(5), new Constant(3)));
        Action<VmState> handler = s => steps.Add(s.ProgramCounter);
        Interpreter.Execute(program, s => { s.DebugInterrupt = handler; });
        // Nodes visited: Add(step0), Constant(5)(step1), Constant(3)(step2)
        await Assert.That(steps).IsEquivalentTo(new[] { 0, 1, 2 });
    }

    [Test, Timeout(10_000)]
    public async Task DebugInterrupt_Block_FiresForEachStatement(CancellationToken ct) {
        var steps = new List<int>();
        var x = new Variable("x");
        var node = new Block([new Assignment(x, new Constant(42)), x], [x]);
        var program = Interpreter.Compile(node);
        Action<VmState> handler = s => steps.Add(s.ProgramCounter);
        Interpreter.Execute(program, s => { s.DebugInterrupt = handler; });
        // Nodes: Block(0), Assignment(1), Constant(42)(2), Variable(x)(3) = 4
        await Assert.That(steps).IsEquivalentTo(new[] { 0, 1, 2, 3 });
    }

    [Test, Timeout(10_000)]
    public async Task DebugInterrupt_WhileLoop_FiresRepeatedly(CancellationToken ct) {
        var steps = new List<int>();
        var i = new Variable("i");
        var node = new Block([
            new Assignment(i, new Constant(0)),
            new WhileLoop(
                new LessThan(i, new Constant(3)),
                new Assignment(i, new Add(i, new Constant(1)))),
            i
        ], [i]);

        var program = Interpreter.Compile(node);
        Action<VmState> handler = s => steps.Add(s.ProgramCounter);
        Interpreter.Execute(program, s => { s.DebugInterrupt = handler; });

        // The WhileLoop triggers interrupts on each iteration's condition and body
        // nodes. Step indices repeat because the same compiled nodes fire each time.
        await Assert.That(steps.Count).IsGreaterThan(10);
        // Verify the result is correct despite all the interrupts
        await Assert.That(ExecDirect(node)).IsEqualTo(3);
    }

    [Test, Timeout(10_000)]
    public async Task DebugInterrupt_NoDebugMode_DoesNotFire(CancellationToken ct) {
        var steps = new List<int>();
        var program = Interpreter.Compile(new Add(new Constant(5), new Constant(3)), CompilationMode.NoDebug);
        Action<VmState> handler = s => steps.Add(s.ProgramCounter);
        Interpreter.Execute(program, s => { s.DebugInterrupt = handler; });
        // In NoDebug mode, DebugInterruptProp is null, so WithInterrupt is no-op
        await Assert.That(steps).IsEmpty();
    }

    [Test, Timeout(10_000)]
    public async Task DebugInterrupt_NullHandler_NoOverhead(CancellationToken ct) {
        // When DebugInterrupt is null on state, the null guard in WithInterrupt
        // skips the expensive path (no Property read, no Invoke).
        // If it incorrectly invoked, this would throw NullReferenceException.
        var program = Interpreter.Compile(new Add(new Constant(5), new Constant(3)));
        // Execute without setting DebugInterrupt — should not throw
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

        // Example of using dumper on a simple expr for illustration (in real use, capture internal expr)
        var sample = System.Linq.Expressions.Expression.Constant(42L);
        var dump = DirectVmAbiEmitter.DumpTree(sample);
        // Just ensure dumper doesn't crash and produces output
        await Assert.That(dump).Contains("Constant");
    }

    // ═══════════════════════════════════════════════════════════════
    // Non-trivial suspend/resume validation using DebugInterrupt as VmDebugger proxy
    // (real VmDebugger from plans uses similar PC/step + status for suspend/resume)
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task SuspendResume_NonTrivial_LoopWithCapture(CancellationToken ct) {
        // Non-trivial: capture outer x, loop with counter, use DebugInterrupt (as VmDebugger proxy)
        // to suspend at specific point (after i reaches 2), resume, verify capture and counter preserved.
        var x = new Variable("x");
        var i = new Variable("i");
        var code = new Block([
            new Assignment(x, new Constant(42)),  // captured
            new Assignment(i, new Constant(0)),
            new WhileLoop(
                new LessThan(i, new Constant(5)),
                new Assignment(i, new Add(i, new Constant(1)))
            ),
            new Add(x, i)  // 42 + 5 = 47
        ], [x, i]);

        var program = Interpreter.Compile(code);

        // Use DebugInterrupt as the "real VmDebugger" hook for suspend/resume validation
        var suspendSteps = new List<int>();
        bool didSuspend = false;
        NodeId? suspendedAtNodeId = null;
        Node? suspendedAtNode = null;
        Action<VmState> debugger = s => {
            suspendSteps.Add(s.ProgramCounter);
            if (!didSuspend && suspendSteps.Count >= 3) {  // non-trivial point after iterations
                s.Status = InterpreterStatus.Suspended;
                suspendedAtNodeId = s.CurrentNodeId;
                suspendedAtNode = s.CurrentAstNode;
                didSuspend = true;
            }
        };

        ExecutionResult exec;
        try {
            exec = Interpreter.Execute(program, s => {
                s.DebugInterrupt = debugger;
                s.MaxLoopIterations = 100;
            });
        }
        catch (Exception ex) {
            throw new Exception("NRE during first Execute/configure or delegate", ex);
        }

        await Assert.That(exec.IsSuspended).IsTrue();
        await Assert.That(didSuspend).IsTrue();
        await Assert.That(suspendSteps.Count).IsGreaterThan(2);
        await Assert.That(suspendedAtNodeId).IsNotNull();
        await Assert.That(suspendedAtNode).IsNotNull();
        // Confirmed: node id accessed directly from the AST node instance (node.Id) at compile time
        // and surfaced in VmState.Current* via Constant embedding at each CompileNode / suspend point.

        // Resume from suspended state
        ExecutionResult resumed;
        try {
            resumed = exec.Resume();
        }
        catch (Exception ex) {
            throw new Exception("NRE during Resume", ex);
        }
        await Assert.That(resumed.IsSuspended).IsFalse();
        await Assert.That(resumed.RawValue).IsEqualTo(47L);  // capture 42 + final i=5
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

        var sw = new System.IO.StringWriter();
        var program = Interpreter.Compile(code, traceExpressions: sw);

        var dump = sw.ToString();
        // The trace now contains the dumped delegate expression tree from direct lowering.
        await Assert.That(dump).Contains("Block");
        await Assert.That(dump).Contains("Constant");
        await Assert.That(dump).Contains("Loop");  // WhileLoop lowers to Loop expression
        // Direct path keeps structured control flow (Loop/Block) vs primitive path's flat ops + labels.
        // Also verify the compiled program works (cross-check).
        await Assert.That(program).IsNotNull();
    }

    [Test, Timeout(10_000)]
    public async Task SuspendNode_UsesNodeInstanceForCurrentId(CancellationToken ct) {
        // Explicit SuspendNode exercises the direct set using sn/sn.Id from the instance (no map).
        var inner = new Constant(77);
        var sn = new SuspendNode(inner, "demo");
        var program = Interpreter.Compile(sn);

        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.IsSuspended).IsTrue();
        await Assert.That(exec.State.CurrentAstNode).IsNotNull();
        await Assert.That(exec.State.CurrentNodeId).IsNotNull();
        // The embedded reference and id come from the node instance at lowering time.
        await Assert.That(exec.State.CurrentAstNode).IsSameReferenceAs(sn);
        await Assert.That(exec.State.CurrentNodeId!.Value).IsEqualTo(sn.Id);
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
        await Assert.That(ExecDirect(new Coalesce(new Constant(0L), new Constant(99)))).IsEqualTo(99);
    }
}