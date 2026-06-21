using System.Diagnostics;
using System.IO;

using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.LoweringPrep;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>Real-world algorithmic tests: AST → Lowering → µops → execute.
/// Exercises the full pipeline on nontrivial programs (loops, recursion,
/// clr calls, etc.).</summary>
public class UopRealWorldTests {
    private static readonly TestTraceWriter? _traceWriter = Debugger.IsAttached ? new() : null;

    private static LoweringResult LowerWith(Node node) {
        var analysis = new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .UseDefiniteAssignmentAnalysis()
            .UseLoweringPreparation()
            .UseUopGeneration()
            .Build()
            .Analyze(node);
        return Lowering.Lower(node, analysis);
    }

    private static InterpreterResult Execute(Node node) {
        using var state = CompileState(node);
        return Vm.Execute(state);
    }

    private static VmState CompileState(Node node) {
        var lowerResult = LowerWith(node);
        var program = ProgramCompiler.Compile(lowerResult, mode: CompilationMode.Normal);
        return new VmState(program) { Trace = _traceWriter, MaxLoopIterations = 100_000_000 };
    }

    // ═══════════════════════════════════════════════════════════════
    //  Fibonacci (iterative)
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Fib_0() => await Assert.That(Fib(0)).IsEqualTo(0L);

    [Test]
    public async Task Fib_1() => await Assert.That(Fib(1)).IsEqualTo(1L);

    [Test]
    public async Task Fib_10() => await Assert.That(Fib(10)).IsEqualTo(55L);

    [Test]
    public async Task Fib_20() => await Assert.That(Fib(20)).IsEqualTo(6765L);

    private static long Fib(int n) {
        // while (i < n) { next = a + b; a = b; b = next; i++; }
        var a = new Variable("a");
        var b = new Variable("b");
        var i = new Variable("i");
        var next = new Variable("next");
        var body = new Block(
            [new Assignment(a, new Constant(0L)),
             new Assignment(b, new Constant(1L)),
             new Assignment(i, new Constant(0L)),
             new WhileLoop(
                 new LessThan(i, new Constant(n)),
                 new Block([
                     new Assignment(next, new Add(a, b)),
                     new Assignment(a, b),
                     new Assignment(b, next),
                     new Assignment(i, new Add(i, new Constant(1)))
                 ])),
             a],
            [a, b, i, next]);
        var result = Execute(new Invoke(new Lambda([], body)));
        return (long)result!.Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Factorial (iterative)
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Fact_0() => await Assert.That(Fact(0)).IsEqualTo(1L);

    [Test]
    public async Task Fact_1() => await Assert.That(Fact(1)).IsEqualTo(1L);

    [Test]
    public async Task Fact_5() => await Assert.That(Fact(5)).IsEqualTo(120L);

    [Test]
    public async Task Fact_10() => await Assert.That(Fact(10)).IsEqualTo(3628800L);

    private static long Fact(int n) {
        var result = new Variable("result");
        var i = new Variable("i");
        var body = new Block(
            [new Assignment(result, new Constant(1L)),
             new Assignment(i, new Constant(1L)),
             new WhileLoop(
                 new LessThanOrEqual(i, new Constant(n)),
                 new Block([
                     new Assignment(result, new Multiply(result, i)),
                     new Assignment(i, new Add(i, new Constant(1)))
                 ])),
             result],
            [result, i]);
        return (long)Execute(new Invoke(new Lambda([], body))).Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Sum of squares: Σ i² for i = 1..n
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task SumSquares_1() => await Assert.That(SumSquares(1)).IsEqualTo(1L);

    [Test]
    public async Task SumSquares_5() => await Assert.That(SumSquares(5)).IsEqualTo(55L);

    [Test]
    public async Task SumSquares_10() => await Assert.That(SumSquares(10)).IsEqualTo(385L);

    private static long SumSquares(int n) {
        var sum = new Variable("sum");
        var i = new Variable("i");
        var body = new Block(
            [new Assignment(sum, new Constant(0L)),
             new Assignment(i, new Constant(1L)),
             new WhileLoop(
                 new LessThanOrEqual(i, new Constant(n)),
                 new Block([
                     new Assignment(sum, new Add(sum, new Multiply(i, i))),
                     new Assignment(i, new Add(i, new Constant(1)))
                 ])),
             sum],
            [sum, i]);
        return (long)Execute(new Invoke(new Lambda([], body))).Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Greatest Common Divisor (Euclidean algorithm)
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Gcd_12_8() => await Assert.That(Gcd(12, 8)).IsEqualTo(4L);

    [Test]
    public async Task Gcd_54_24() => await Assert.That(Gcd(54, 24)).IsEqualTo(6L);

    [Test]
    public async Task Gcd_101_10() => await Assert.That(Gcd(101, 10)).IsEqualTo(1L);

    private static long Gcd(int a, int b) {
        var x = new Variable("x");
        var y = new Variable("y");
        var tmp = new Variable("tmp");
        // while (y != 0) { tmp = x % y; x = y; y = tmp; }
        var body = new Block(
            [new Assignment(x, new Constant((long)a)),
             new Assignment(y, new Constant((long)b)),
             new WhileLoop(
                 new NotEqual(y, new Constant(0L)),
                 new Block([
                     new Assignment(tmp, new Modulo(x, y)),
                     new Assignment(x, y),
                     new Assignment(y, tmp)
                 ])),
             x],
            [x, y, tmp]);
        return (long)Execute(new Invoke(new Lambda([], body))).Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  CLR-powered: Math.Pow via repeated multiplication
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Power_2_0() => await Assert.That(Power(2, 0)).IsEqualTo(1L);

    [Test]
    public async Task Power_2_10() => await Assert.That(Power(2, 10)).IsEqualTo(1024L);

    [Test]
    public async Task Power_3_5() => await Assert.That(Power(3, 5)).IsEqualTo(243L);

    private static long Power(int b, int e) {
        var result = new Variable("result");
        var i = new Variable("i");
        var body = new Block(
            [new Assignment(result, new Constant(1L)),
             new Assignment(i, new Constant(0L)),
             new WhileLoop(
                 new LessThan(i, new Constant(e)),
                 new Block([
                     new Assignment(result, new Multiply(result, new Constant((long)b))),
                     new Assignment(i, new Add(i, new Constant(1)))
                 ])),
             result],
            [result, i]);
        return (long)Execute(new Invoke(new Lambda([], body))).Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  CLR call chain: Math.Max on a range
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task ClrMaxChain_50() {
        var maxMethod = new Member(
            new TypeReference(typeof(Math).FullName!), nameof(Math.Max));
        Node chain = new Constant(1);
        for (int i = 2; i <= 50; i++)
            chain = new Invoke(maxMethod, chain, new Constant(i));
        var result = Execute(chain);
        await Assert.That(result.Value).IsEqualTo(50L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Deep balanced sum: synthetic stress for lowering + dispatch
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task DeepSum_5000() {
        var node = BuildDeepSum(5000);
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(12502500L);
    }

    private static Node BuildDeepSum(int n) {
        var values = new int[n];
        for (int i = 0; i < n; i++) values[i] = i + 1;
        return BuildBalanced(values, 0, n - 1);
    }

    private static Node BuildBalanced(int[] values, int start, int end) {
        if (start == end) return new Constant(values[start]);
        int mid = (start + end) / 2;
        return new Add(BuildBalanced(values, start, mid), BuildBalanced(values, mid + 1, end));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Count digits: while loop with division
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task CountDigits_0() => await Assert.That(CountDigits(0)).IsEqualTo(1L);

    [Test]
    public async Task CountDigits_12345() => await Assert.That(CountDigits(12345)).IsEqualTo(5L);

    [Test]
    public async Task CountDigits_1000000() => await Assert.That(CountDigits(1000000)).IsEqualTo(7L);

    private static long CountDigits(int n) {
        var num = new Variable("num");
        var count = new Variable("count");
        var body = new Block(
            [new Assignment(num, new Constant((long)n)),
             new Assignment(count, new Constant(0L)),
             new WhileLoop(
                 new GreaterThan(num, new Constant(0L)),
                 new Block([
                     new Assignment(num, new Divide(num, new Constant(10L))),
                     new Assignment(count, new Add(count, new Constant(1L)))
                 ])),
             new Conditional(
                 new Equal(count, new Constant(0L)),
                 new Constant(1L),
                 count)],
            [num, count]);
        return (long)Execute(new Invoke(new Lambda([], body))).Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Reverse a number (arithmetic)
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Reverse_123() => await Assert.That(Reverse(123)).IsEqualTo(321L);

    [Test]
    public async Task Reverse_100() => await Assert.That(Reverse(100)).IsEqualTo(1L);

    [Test]
    public async Task Reverse_987654321() => await Assert.That(Reverse(987654321)).IsEqualTo(123456789L);

    private static long Reverse(int n) {
        var num = new Variable("num");
        var rev = new Variable("rev");
        var body = new Block(
            [new Assignment(num, new Constant((long)n)),
             new Assignment(rev, new Constant(0L)),
             new WhileLoop(
                 new GreaterThan(num, new Constant(0L)),
                 new Block([
                     new Assignment(rev, new Add(
                         new Multiply(rev, new Constant(10L)),
                         new Modulo(num, new Constant(10L)))),
                     new Assignment(num, new Divide(num, new Constant(10L)))
                 ])),
             rev],
            [num, rev]);
        return (long)Execute(new Invoke(new Lambda([], body))).Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Triangular numbers: T(n) = n * (n + 1) / 2
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Triangular_10() {
        // T(10) = 55 via formula (not loop)
        var node = new Divide(
            new Multiply(new Constant(10L), new Constant(11L)),
            new Constant(2L));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(55L);
    }

    [Test]
    public async Task Triangular_100() {
        var node = new Divide(
            new Multiply(new Constant(100L), new Constant(101L)),
            new Constant(2L));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(5050L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Multiple lambdas: mapping a binary function over arguments
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task TwoLambdas_Independent() {
        // double(x) = x * 2 and triple(x) = x * 3 — each called once
        var doubleProg = LowerWith(new Invoke(
            new Lambda([new Parameter("x", TypeReference.To<int>())],
                new Multiply(new Variable("x"), new Constant(2L))),
            new Constant(7)));
        await Assert.That(Execute(new Invoke(
            new Lambda([new Parameter("x", TypeReference.To<int>())],
                new Multiply(new Variable("x"), new Constant(2L))),
            new Constant(7))).Value).IsEqualTo(14L);

        await Assert.That(Execute(new Invoke(
            new Lambda([new Parameter("x", TypeReference.To<int>())],
                new Multiply(new Variable("x"), new Constant(3L))),
            new Constant(9))).Value).IsEqualTo(27L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Prime sieve (trial division) — count primes ≤ n
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task CountPrimes_10() => await Assert.That(CountPrimes(10)).IsEqualTo(4L);

    [Test]
    public async Task CountPrimes_100() => await Assert.That(CountPrimes(100)).IsEqualTo(25L);

    [Test]
    public async Task CountPrimes_1000() => await Assert.That(CountPrimes(1000)).IsEqualTo(168L);

    private static long CountPrimes(int limit) {
        var n = new Variable("n");
        var i = new Variable("i");
        var count = new Variable("count");
        var isPrime = new Variable("isPrime");
        var body = new Block(
            [new Assignment(count, new Constant(0L)),
             new Assignment(n, new Constant(2L)),
             // for n = 2..limit
             new WhileLoop(new LessThanOrEqual(n, new Constant(limit)),
                 new Block([
                     // isPrime = true; i = 2; while (i * i <= n && isPrime) { if (n % i == 0) isPrime = false; i++; }
                     new Assignment(isPrime, new Constant(1L)),
                     new Assignment(i, new Constant(2L)),
                     new WhileLoop(
                         new And(
                             new LessThanOrEqual(new Multiply(i, i), n),
                             new Equal(isPrime, new Constant(1L))),
                         new Block([
                             // if (n % i == 0) isPrime = 0
                             new Assignment(isPrime, new Conditional(
                                 new Equal(new Modulo(n, i), new Constant(0L)),
                                 new Constant(0L), isPrime)),
                             new Assignment(i, new Add(i, new Constant(1L)))
                         ])),
                     // if (isPrime) count++
                     // count += isPrime ? 1 : 0
                     new Assignment(count, new Add(count,
                         new Conditional(new Equal(isPrime, new Constant(1L)),
                             new Constant(1L), new Constant(0L)))),
                     new Assignment(n, new Add(n, new Constant(1L)))
                 ])),
             count],
            [n, i, count, isPrime]);
        return (long)Execute(new Invoke(new Lambda([], body))).Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Composable math "stdlib" — functions built from primitives
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Stdlib_IsEven_ReturnsTrueForEven() {
        // isEven = x => x % 2 == 0
        var x = new Parameter("x", TypeReference.To<int>());
        var isEven = new Lambda([x],
            new Equal(new Modulo(x, new Constant(2L)), new Constant(0L)));
        var result = Execute(new Invoke(isEven, new Constant(10)));
        await Assert.That(result.Value).IsEqualTo(1L);
    }

    [Test]
    public async Task Stdlib_IsEven_ReturnsFalseForOdd() {
        var x = new Parameter("x", TypeReference.To<int>());
        var isEven = new Lambda([x],
            new Equal(new Modulo(x, new Constant(2L)), new Constant(0L)));
        var result = Execute(new Invoke(isEven, new Constant(7)));
        await Assert.That(result.Value).IsEqualTo(0L);
    }

    [Test]
    public async Task Stdlib_Abs_ViaConditional() {
        // abs = x => x < 0 ? -x : x
        var x = new Parameter("x", TypeReference.To<int>());
        var abs = new Lambda([x],
            new Conditional(
                new LessThan(x, new Constant(0L)),
                new UnaryMinus(x),
                x));
        var result = Execute(new Invoke(abs, new Constant(-5)));
        await Assert.That(result.Value).IsEqualTo(5L);
    }

    [Test]
    public async Task Stdlib_Abs_PositiveUnchanged() {
        var x = new Parameter("x", TypeReference.To<int>());
        var abs = new Lambda([x],
            new Conditional(
                new LessThan(x, new Constant(0L)),
                new UnaryMinus(x),
                x));
        var result = Execute(new Invoke(abs, new Constant(42)));
        await Assert.That(result.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task Stdlib_Clamp() {
        // clamp = (x, lo, hi) => x < lo ? lo : (x > hi ? hi : x)
        var x = new Parameter("x", TypeReference.To<int>());
        var lo = new Parameter("lo", TypeReference.To<int>());
        var hi = new Parameter("hi", TypeReference.To<int>());
        var clamp = new Lambda([x, lo, hi],
            new Conditional(
                new LessThan(x, lo),
                lo,
                new Conditional(new GreaterThan(x, hi), hi, x)));
        var r1 = Execute(new Invoke(clamp, new Constant(-5), new Constant(0), new Constant(100)));
        await Assert.That(r1.Value).IsEqualTo(0L);
        var r2 = Execute(new Invoke(clamp, new Constant(50), new Constant(0), new Constant(100)));
        await Assert.That(r2.Value).IsEqualTo(50L);
        var r3 = Execute(new Invoke(clamp, new Constant(200), new Constant(0), new Constant(100)));
        await Assert.That(r3.Value).IsEqualTo(100L);
    }

    [Test]
    public async Task Mandelbrot_128_Compare() {
        const int size = 128;
        const int S = 8;
        var x = new Variable("x"); var y = new Variable("y");
        var zx = new Variable("zx"); var zy = new Variable("zy");
        var zx2 = new Variable("zx2"); var zy2 = new Variable("zy2");
        var iter = new Variable("iter"); var total = new Variable("total");

        Node Cx(Node xv) => new Subtract(new Multiply(xv, new Constant(8L)), new Constant(size * 4L));
        Node Cy(Node yv) => Cx(yv);

        Node mandelPixel = new Block([
            new Assignment(zx, new Constant(0L)),
            new Assignment(zy, new Constant(0L)),
            new Assignment(iter, new Constant(0L)),
            new WhileLoop(
                new And(
                    new LessThan(iter, new Constant(256)),
                    new LessThanOrEqual(
                        new Add(
                            new ShiftRight(new Multiply(zx, zx), new Constant(S)),
                            new ShiftRight(new Multiply(zy, zy), new Constant(S))),
                        new Constant(4 << S))),
                new Block([
                    new Assignment(zx2, new Add(
                        new Subtract(
                            new ShiftRight(new Multiply(zx, zx), new Constant(S)),
                            new ShiftRight(new Multiply(zy, zy), new Constant(S))),
                        Cx(x))),
                    new Assignment(zy, new Add(
                        new ShiftRight(new Multiply(
                            new Multiply(zx, new Constant(2L)), zy), new Constant(S)),
                        Cy(y))),
                    new Assignment(zx, zx2),
                    new Assignment(iter, new Add(iter, new Constant(1L)))
                ])),
            iter
        ]);

        var body = new Invoke(new Lambda([], new Block(
            [new Assignment(total, new Constant(0L)),
             new Assignment(y, new Constant(0L)),
             new WhileLoop(new LessThan(y, new Constant(size)),
                 new Block([
                     new Assignment(x, new Constant(0L)),
                     new WhileLoop(new LessThan(x, new Constant(size)),
                         new Block([
                             new Assignment(total, new Add(total, mandelPixel)),
                             new Assignment(x, new Add(x, new Constant(1L)))
                         ])),
                     new Assignment(y, new Add(y, new Constant(1L)))
                 ])),
             total],
            [x, y, zx, zy, zx2, zy2, iter, total])));

        using var state = CompileState(body);
        Vm.Execute(state);
        long result = (long)state.Stack.Pop();
        await Assert.That(result).IsEqualTo(458080L);
    }

    [Test]
    public async Task NQueens_8_Compare() {
        var stack = new Variable("stack");
        var sp = new Variable("sp");
        var total = new Variable("total");
        var ld = new Variable("ld"); var cols = new Variable("cols");
        var rd = new Variable("rd");
        var avail = new Variable("avail"); var bit = new Variable("bit");
        const int boardSize = 8;
        long allBits = (1L << boardSize) - 1;
        int maxDepth = boardSize;
        int stackSize = maxDepth * maxDepth * maxDepth * 3;

        Node St(Node idx) => new IndexAccess(stack, idx);
        Node L(long v) => new Constant(v);

        var body = new Invoke(new Lambda([], new Block(
            [new Assignment(stack, new NewArray(TypeReference.To<long>(), new Constant(stackSize))),
             new Assignment(sp, L(0)),
             new Assignment(total, L(0)),
             new Assignment(St(sp), L(0)),
             new Assignment(St(new Add(sp, L(1))), L(0)),
             new Assignment(St(new Add(sp, L(2))), L(0)),
             new Assignment(sp, new Add(sp, L(3))),
             new WhileLoop(new GreaterThan(sp, L(0)), new Block([
                 new Assignment(sp, new Subtract(sp, L(3))),
                 new Assignment(ld, St(sp)),
                 new Assignment(cols, St(new Add(sp, L(1)))),
                 new Assignment(rd, St(new Add(sp, L(2)))),
                 new IfStatement(new Equal(cols, L(allBits)),
                     new Assignment(total, new Add(total, L(1)))),
                 new Assignment(avail, new BitwiseAnd(
                     new BitwiseNot(new BitwiseOr(new BitwiseOr(ld, cols), rd)),
                     L(allBits))),
                 new WhileLoop(new NotEqual(avail, L(0)), new Block([
                     new Assignment(bit, new BitwiseAnd(new UnaryMinus(avail), avail)),
                     new Assignment(avail, new BitwiseXor(avail, bit)),
                     new Assignment(St(sp),
                         new ShiftLeft(new BitwiseOr(ld, bit), L(1))),
                     new Assignment(St(new Add(sp, L(1))),
                         new BitwiseOr(cols, bit)),
                     new Assignment(St(new Add(sp, L(2))),
                         new ShiftRight(new BitwiseOr(rd, bit), L(1))),
                     new Assignment(sp, new Add(sp, L(3))),
                 ])),
             ])),
             total],
            [stack, sp, total, ld, cols, rd, avail, bit])));

        using var state = CompileState(body);
        Vm.Execute(state);
        long result = (long)state.Stack.Pop();
        await Assert.That(result).IsEqualTo(92L);
    }

    [Test]
    public async Task Collatz_10000_Compare() {
        const int limit = 10000;
        var n = new Variable("n"); var i = new Variable("i");
        var len = new Variable("len"); var maxLen = new Variable("maxLen");
        var bestN = new Variable("bestN");

        var body = new Invoke(new Lambda([], new Block(
            [new Assignment(maxLen, new Constant(0L)),
             new Assignment(bestN, new Constant(0L)),
             new Assignment(n, new Constant(1L)),
             new WhileLoop(new LessThanOrEqual(n, new Constant(limit)),
                 new Block([
                     new Assignment(len, new Constant(0L)),
                     new Assignment(i, n),
                     new WhileLoop(new NotEqual(i, new Constant(1L)),
                         new Block([
                             new Assignment(i, new Conditional(
                                 new Equal(new Modulo(i, new Constant(2L)), new Constant(0L)),
                                 new ShiftRight(i, new Constant(1)),
                                 new Add(new Multiply(i, new Constant(3L)), new Constant(1L)))),
                             new Assignment(len, new Add(len, new Constant(1L)))
                         ])),
                     new IfStatement(
                         new GreaterThan(len, maxLen),
                         new Block([
                             new Assignment(maxLen, len),
                             new Assignment(bestN, n)
                         ])),
                     new Assignment(n, new Add(n, new Constant(1L)))
                 ])),
             new BitwiseOr(new ShiftLeft(bestN, new Constant(32L)), maxLen)],
            [n, i, len, maxLen, bestN])));

        using var state = CompileState(body);
        Vm.Execute(state);
        long packed = (long)state.Stack.Pop();
        long bestNResult = packed >> 32;
        long maxLenResult = packed & 0xFFFFFFFFL;
        await Assert.That(bestNResult).IsEqualTo(6171L);
        await Assert.That(maxLenResult).IsEqualTo(261L);
    }
}