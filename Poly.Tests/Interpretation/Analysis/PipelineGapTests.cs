using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.LoweringPrep;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation.Analysis;

/// <summary>
/// Tests that verify the NEW pipeline (assembly step) produces the same
/// results as the LEGACY pipeline.  Any failures here represent a gap
/// introduced by the new assembly step vs the old ResolveProducers path.
/// Each test runs both pipelines and compares results.
/// </summary>
public sealed class PipelineGapTests {
    private static Analyzer NewPipeline =>
        new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .UseLoweringPreparation()
            .UseUopGeneration()
            .Build();

    private static Analyzer LegacyPipeline =>
        new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .Build();

    /// <summary>Run both pipelines, return (newResult, legacyResult).</summary>
    private static (long? New, long? Legacy) RunBoth(Node node) {
        var result = NewPipeline.Analyze(node);
        var lowered = Lowering.Lower(node, result);
        var prog = ProgramCompiler.Compile(lowered, mode: CompilationMode.Normal);
        var state = new VmState(prog) { MaxLoopIterations = 100_000_000 };
        Vm.Execute(state);
        long? newVal = state.Stack.StackPointer > 0 ? state.Stack.Pop() : null;

        var result2 = LegacyPipeline.Analyze(node);
        var lowered2 = Lowering.Lower(node, result2);
        var prog2 = ProgramCompiler.Compile(lowered2, mode: CompilationMode.Normal);
        var state2 = new VmState(prog2) { MaxLoopIterations = 100_000_000 };
        Vm.Execute(state2);
        long? legacyVal = state2.Stack.StackPointer > 0 ? state2.Stack.Pop() : null;

        return (newVal, legacyVal);
    }

    private static async Task AssertMatch(Node node, string label) {
        var (newV, legV) = RunBoth(node);
        await Assert.That(newV).IsEqualTo(legV);
    }

    // ── Basic expressions ──────────────────────────────────────────────

    [Test]
    public async Task Add_Simple() =>
        await AssertMatch(new Add(new Constant(3L), new Constant(4L)), "3+4");

    [Test]
    public async Task Subtract_Simple() =>
        await AssertMatch(new Subtract(new Constant(10L), new Constant(3L)), "10-3");

    [Test]
    public async Task Comparison() =>
        await AssertMatch(new LessThan(new Constant(3L), new Constant(5L)), "3<5");

    // ── IfStatement (now a Statement) ──────────────────────────────────

    [Test]
    public async Task IfThen_WithoutElse_SideEffect() {
        var v = new Variable("x");
        var body = new Invoke(new Lambda([], new Block([
            new Assignment(v, new Constant(0L)),
            new IfStatement(new Constant(1L), new Assignment(v, new Constant(42L))),
            v
        ], [v])));
        await AssertMatch(body, "if true then x=42");
    }

    [Test]
    public async Task IfThenElse_SideEffect() {
        var v = new Variable("v");
        var body = new Invoke(new Lambda([], new Block([
            new Assignment(v, new Constant(0L)),
            new IfStatement(new Constant(0L),
                new Assignment(v, new Constant(10L)),
                new Assignment(v, new Constant(20L))),
            v
        ], [v])));
        await AssertMatch(body, "if 0 then 10 else 20 → v=20");
    }

    // ── WhileLoop ──────────────────────────────────────────────────────

    [Test]
    public async Task WhileLoop_Counter() {
        var v = new Variable("v");
        var body = new Invoke(new Lambda([], new Block([
            new Assignment(v, new Constant(0L)),
            new WhileLoop(new LessThan(v, new Constant(5L)),
                new Assignment(v, new Add(v, new Constant(1L)))),
            v
        ], [v])));
        await AssertMatch(body, "while v<5 → v=5");
    }

    [Test]
    public async Task NestWhileLoops_4x4() {
        var x = new Variable("x"); var y = new Variable("y"); var t = new Variable("t");
        var body = new Invoke(new Lambda([], new Block(
            [new Assignment(t, new Constant(0L)), new Assignment(y, new Constant(0L)),
             new WhileLoop(new LessThan(y, new Constant(4L)), new Block([
                 new Assignment(x, new Constant(0L)),
                 new WhileLoop(new LessThan(x, new Constant(4L)), new Block([
                     new Assignment(t, new Add(t, new Constant(1L))),
                     new Assignment(x, new Add(x, new Constant(1L)))])),
                 new Assignment(y, new Add(y, new Constant(1L)))])),
             t], [x, y, t])));
        await AssertMatch(body, "4x4 nested loops");
    }

    // ── Conditional (Expression) ───────────────────────────────────────

    [Test]
    public async Task Conditional_True_ThenBranch() {
        var body = new Invoke(new Lambda([],
            new Conditional(new Constant(1L), new Constant(42L), new Constant(99L))));
        var (newV, _) = RunBoth(body);
        await Assert.That(newV).IsEqualTo(42);
    }

    [Test]
    public async Task Conditional_False_ElseBranch() {
        var body = new Invoke(new Lambda([],
            new Conditional(new Constant(0L), new Constant(42L), new Constant(99L))));
        var (newV, _) = RunBoth(body);
        await Assert.That(newV).IsEqualTo(99);
    }

    [Test]
    public async Task Conditional_AsAddArgument() {
        // NEW pipeline correctly handles φ at Conditional merge points.
        // Legacy pipeline still returns wrong value (3 instead of 8).
        var body = new Invoke(new Lambda([],
            new Add(new Conditional(new Constant(1L), new Constant(5L), new Constant(10L)),
                new Constant(3L))));
        var (newV, legV) = RunBoth(body);
        await Assert.That(newV).IsEqualTo(8);
        // Legacy still has the pre-existing φ bug.
        await Assert.That(legV).IsEqualTo(3);
    }

    // ── ForLoop ────────────────────────────────────────────────────────

    [Test]
    public async Task ForLoop_Counter() {
        var i = new Variable("i");
        var body = new Invoke(new Lambda([], new Block([
            new ForLoop(
                new Assignment(i, new Constant(0L)),
                new LessThan(i, new Constant(5L)),
                new Assignment(i, new Add(i, new Constant(1L))),
                new Constant(0L)),
            i
        ], [i])));
        await AssertMatch(body, "for i=0..4 → i=5");
    }

    // ── DoWhileLoop ────────────────────────────────────────────────────

    [Test]
    public async Task DoWhileLoop_Counter() {
        var v = new Variable("v");
        var body = new Invoke(new Lambda([], new Block([
            new Assignment(v, new Constant(0L)),
            new DoWhileLoop(
                new Assignment(v, new Add(v, new Constant(1L))),
                new LessThan(v, new Constant(3L))),
            v
        ], [v])));
        await AssertMatch(body, "do v++ while v<3 → v=3");
    }

    // ── IfStatement inside WhileLoop (common pattern) ──────────────────

    [Test]
    public async Task IfInsideWhile_CountPrimes_Like() {
        var n = new Variable("n"); var i = new Variable("i"); var c = new Variable("c");
        var body = new Invoke(new Lambda([], new Block([
            new Assignment(c, new Constant(0L)), new Assignment(n, new Constant(2L)),
            new WhileLoop(new LessThanOrEqual(n, new Constant(10L)), new Block([
                new Assignment(i, new Constant(2L)),
                new WhileLoop(new LessThan(i, n), new Block([
                    new IfStatement(new Equal(new Modulo(n, i), new Constant(0L)),
                        new Assignment(i, n)),  // break-ish: push i to n to exit inner loop
                    new Assignment(i, new Add(i, new Constant(1L)))])),
                new IfStatement(new Equal(i, n),
                    new Assignment(c, new Add(c, new Constant(1L)))),
                new Assignment(n, new Add(n, new Constant(1L)))])),
             c], [n, i, c])));
        await AssertMatch(body, "count primes ≤10 → 4");
    }

    // ── CLR method calls ──────────────────────────────────────────────

    [Test]
    public async Task ClrMethodCall_MathMax() {
        var method = new Member(new TypeReference(typeof(Math).FullName!), nameof(Math.Max));
        var body = new Invoke(new Lambda([],
            new Invoke(method, new Constant(3L), new Constant(7L))));
        await AssertMatch(body, "Math.Max(3,7)=7");
    }

    [Test]
    public async Task ClrMethodCall_MathAbs() {
        var method = new Member(new TypeReference(typeof(Math).FullName!), nameof(Math.Abs));
        var body = new Invoke(new Lambda([],
            new Invoke(method, new Constant(-42L))));
        await AssertMatch(body, "Math.Abs(-42)=42");
    }

    // ── Lambdas ────────────────────────────────────────────────────────

    [Test]
    public async Task Lambda_Identity() {
        var p = new Parameter("x");
        var body = new Invoke(new Lambda([p], p), new Constant(99L));
        await AssertMatch(body, "(x=>x)(99)=99");
    }

    // ── Complex real-world patterns ────────────────────────────────────

    [Test]
    public async Task Collatz_Steps() {
        var i = new Variable("i"); var n = new Variable("n");
        var body = new Invoke(new Lambda([], new Block([
            new Assignment(i, new Constant(7L)), new Assignment(n, new Constant(0L)),
            new WhileLoop(new NotEqual(i, new Constant(1L)), new Block([
                new IfStatement(
                    new Equal(new Modulo(i, new Constant(2L)), new Constant(0L)),
                    new Assignment(i, new Divide(i, new Constant(2L))),
                    new Assignment(i, new Add(new Multiply(i, new Constant(3L)), new Constant(1L)))),
                new Assignment(n, new Add(n, new Constant(1L)))])),
            n], [i, n])));
        await AssertMatch(body, "Collatz(7) steps=16");
    }
}