using BenchmarkDotNet.Attributes;

using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.VirtualMachine;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Benchmarks;

/// <summary>Micro-benchmarks for individual compiled µops (no lowering, no analysis).</summary>
[MemoryDiagnoser]
public class OpcodeBenchmarks {
    private Action<VmState>? _cPush, _cAdd, _cSub, _cMul, _cEq, _cAddImm, _cNot, _cNeg, _cDup, _cPop;
    private Action<VmState>? _cLoadLocal, _cStoreLocal, _cIncLocal, _cLoadArg;

    [GlobalSetup]
    public void Setup() {
        _cPush = ProgramCompiler.Compile(new MicroOp[] { new PushOp(42L) });
        _cAdd = ProgramCompiler.Compile(new MicroOp[] { new PushOp(10L), new PushOp(20L), new AddOp() });
        _cSub = ProgramCompiler.Compile(new MicroOp[] { new PushOp(50L), new PushOp(20L), new SubOp() });
        _cMul = ProgramCompiler.Compile(new MicroOp[] { new PushOp(7L), new PushOp(8L), new MulOp() });
        _cEq = ProgramCompiler.Compile(new MicroOp[] { new PushOp(10L), new PushOp(10L), new EqOp() });
        _cAddImm = ProgramCompiler.Compile(new MicroOp[] { new PushOp(10L), new AddImmOp(5L) });
        _cNot = ProgramCompiler.Compile(new MicroOp[] { new PushOp(0L), new NotOp() });
        _cNeg = ProgramCompiler.Compile(new MicroOp[] { new PushOp(42L), new NegOp() });
        _cDup = ProgramCompiler.Compile(new MicroOp[] { new PushOp(42L), new DupOp() });
        _cPop = ProgramCompiler.Compile(new MicroOp[] { new PushOp(10L), new PushOp(20L), new PopOp() });

        _cLoadLocal = ProgramCompiler.Compile(new MicroOp[] { new LoadLocalOp(0) });
        _cStoreLocal = ProgramCompiler.Compile(new MicroOp[] { new PushOp(42L), new StoreLocalOp(0) });
        _cIncLocal = ProgramCompiler.Compile(new MicroOp[] { new PushOp(0L), new StoreLocalOp(0), new IncLocalOp(0, 1L) });
        _cLoadArg = ProgramCompiler.Compile(new MicroOp[] { new LoadArgOp(1) });
    }

    private const int R = 5000;

    private static long RunCompiled(Action<VmState> del) {
        using var s = new VmState();
        long r = 0;
        for (int i = 0; i < R; i++) { s.Reset(); del(s); r += s.Stack.SP > 0 ? s.Stack.Pop() : 0; }
        return r;
    }

    [Benchmark(OperationsPerInvoke = R)]
    public long Push_Compiled() => RunCompiled(_cPush!);
    [Benchmark(OperationsPerInvoke = R)]
    public long Add_Compiled() => RunCompiled(_cAdd!);
    [Benchmark(OperationsPerInvoke = R)]
    public long Sub_Compiled() => RunCompiled(_cSub!);
    [Benchmark(OperationsPerInvoke = R)]
    public long Mul_Compiled() => RunCompiled(_cMul!);
    [Benchmark(OperationsPerInvoke = R)]
    public long Eq_Compiled() => RunCompiled(_cEq!);
    [Benchmark(OperationsPerInvoke = R)]
    public long AddImm_Compiled() => RunCompiled(_cAddImm!);
    [Benchmark(OperationsPerInvoke = R)]
    public long Not_Compiled() => RunCompiled(_cNot!);
    [Benchmark(OperationsPerInvoke = R)]
    public long Neg_Compiled() => RunCompiled(_cNeg!);
    [Benchmark(OperationsPerInvoke = R)]
    public long Dup_Compiled() => RunCompiled(_cDup!);
    [Benchmark(OperationsPerInvoke = R)]
    public long Pop_Compiled() => RunCompiled(_cPop!);
}

/// <summary>Full-pipeline benchmarks: AST → Lowering → µops → execute.
/// Measures real-world algorithmic patterns end-to-end.</summary>
[MemoryDiagnoser]
public class UopBenchmarks {
    private VmState? _state;

    // Loop sum programs
    private Bytecode _loopSum1000 = null!;
    private Bytecode _loopSum10000 = null!;

    // Fibonacci programs
    private Bytecode _fib10 = null!;
    private Bytecode _fib20 = null!;

    // Factorial
    private Bytecode _fact10 = null!;

    // Deep balanced sum (no loops, pure arithmetic dispatch)
    private Bytecode _deepSum1000 = null!;
    private Bytecode _deepSum10000 = null!;

    // CLR call chain
    private Bytecode _clrChain100 = null!;

    // String concatenation (uses CLR string.Concat)
    private Bytecode _stringConcat = null!;

    // Gcd
    private Bytecode _gcd = null!;

    // Prime sieve (trial division)
    private Bytecode _countPrimes100 = null!;
    private Bytecode _countPrimes1000 = null!;

    // Sieve of Eratosthenes (BitArray)
    private Bytecode _sieve100 = null!;
    private Bytecode _sieve1M = null!;
    private Bytecode _sieve1B = null!;

    // Mandelbrot (fixed-point integer)
    private Bytecode _mandelbrot = null!;
    private Bytecode _nqueens = null!;

    // Collatz max sequence length
    private Bytecode _collatz = null!;

    [GlobalSetup]
    public void Setup() {
        _state = new VmState();

        _loopSum1000 = Lower(BuildLoopSum(1000));
        _loopSum10000 = Lower(BuildLoopSum(10000));
        _fib10 = Lower(BuildFib(10));
        _fib20 = Lower(BuildFib(20));
        _fact10 = Lower(BuildFact(10));
        _deepSum1000 = Lower(BuildDeepSum(1000));
        _deepSum10000 = Lower(BuildDeepSum(10000));
        _clrChain100 = Lower(BuildClrChain(100));
        _stringConcat = Lower(new Add(new Constant("Hello, "), new Constant("World")));
        _gcd = Lower(BuildGcd(123456, 7890));
        _countPrimes100 = Lower(BuildCountPrimes(100));
        _countPrimes1000 = Lower(BuildCountPrimes(1000));
        _sieve100 = Lower(BuildSieve(100000));
        _sieve1M = Lower(BuildSieve(1000000));
        _sieve1B = Lower(BuildSieve(1000000000));
        _mandelbrot = Lower(BuildMandelbrot(128));
        _nqueens = Lower(BuildNQueens(8));
        _collatz = Lower(BuildCollatz(1000));
        // NQueens disabled — recursive Lambda has circular type resolution issues
    }

    [GlobalCleanup]
    public void Cleanup() => _state?.Dispose();

    private static Bytecode Lower(Node node) {
        var result = new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .Build()
            .Analyze(node);
        return Lowering.Lower(node, result);
    }

    private object? Exec(Bytecode prog) {
        _state!.Program = prog;
        _state.Reset();
        return Vm.Execute(_state).Value;
    }

    // ── Loop sum: Σ 1..n ──

    [Benchmark]
    public object? LoopSum_1000() => Exec(_loopSum1000);
    [Benchmark]
    public object? LoopSum_10000() => Exec(_loopSum10000);

    // ── Fibonacci (iterative) ──

    [Benchmark]
    public object? Fib_10() => Exec(_fib10);
    [Benchmark]
    public object? Fib_20() => Exec(_fib20);

    // ── Factorial ──

    [Benchmark]
    public object? Fact_10() => Exec(_fact10);

    // ── Deep sum (balanced tree, no loops) ──

    [Benchmark]
    public object? DeepSum_1000() => Exec(_deepSum1000);
    [Benchmark]
    public object? DeepSum_10000() => Exec(_deepSum10000);

    // ── CLR call chain ──

    [Benchmark]
    public object? ClrChain_100() => Exec(_clrChain100);

    // ── String concat ──

    [Benchmark]
    public object? StringConcat() => Exec(_stringConcat);

    // ── GCD (Euclidean algorithm) ──

    [Benchmark]
    public object? Gcd() => Exec(_gcd);

    [Benchmark]
    public object? CountPrimes_100() => Exec(_countPrimes100);

    [Benchmark]
    public object? CountPrimes_1000() => Exec(_countPrimes1000);

    [Benchmark]
    public object? Sieve_100K() => Exec(_sieve100);

    [Benchmark]
    public object? Sieve_1M() => Exec(_sieve1M);

    [Benchmark]
    public object? Sieve_1B() => Exec(_sieve1B);

    [Benchmark]
    public object? Mandelbrot() => Exec(_mandelbrot);

    [Benchmark]
    public object? NQueens() => Exec(_nqueens);

    [Benchmark]
    public object? Collatz() => Exec(_collatz);

    // ═══════════════════════════════════════════════════
    //  Builders
    // ═══════════════════════════════════════════════

    private static Node BuildDeepSum(int n) {
        var vals = new int[n];
        for (int i = 0; i < n; i++) vals[i] = i + 1;
        return BuildBalanced(vals, 0, n - 1);
    }

    private static Node BuildBalanced(int[] v, int s, int e) {
        if (s == e) return new Constant(v[s]);
        int m = (s + e) / 2;
        return new Add(BuildBalanced(v, s, m), BuildBalanced(v, m + 1, e));
    }

    private static Node BuildLoopSum(int n) {
        var sum = new Variable("sum");
        var i = new Variable("i");
        return new Invoke(new Lambda([], new Block(
            [new Assignment(sum, new Constant(0L)),
             new Assignment(i, new Constant(1L)),
             new WhileLoop(new LessThanOrEqual(i, new Constant(n)),
                 new Block([
                     new Assignment(sum, new Add(sum, i)),
                     new Assignment(i, new Add(i, new Constant(1L)))
                 ])),
             sum],
            [sum, i])));
    }

    private static Node BuildFib(int n) {
        var a = new Variable("a");
        var b = new Variable("b");
        var i = new Variable("i");
        var next = new Variable("next");
        return new Invoke(new Lambda([], new Block(
            [new Assignment(a, new Constant(0L)),
             new Assignment(b, new Constant(1L)),
             new Assignment(i, new Constant(0L)),
             new WhileLoop(new LessThan(i, new Constant(n)),
                 new Block([
                     new Assignment(next, new Add(a, b)),
                     new Assignment(a, b),
                     new Assignment(b, next),
                     new Assignment(i, new Add(i, new Constant(1L)))
                 ])),
             a],
            [a, b, i, next])));
    }

    private static Node BuildFact(int n) {
        var r = new Variable("r");
        var i = new Variable("i");
        return new Invoke(new Lambda([], new Block(
            [new Assignment(r, new Constant(1L)),
             new Assignment(i, new Constant(1L)),
             new WhileLoop(new LessThanOrEqual(i, new Constant(n)),
                 new Block([
                     new Assignment(r, new Multiply(r, i)),
                     new Assignment(i, new Add(i, new Constant(1L)))
                 ])),
             r],
            [r, i])));
    }

    private static Node BuildClrChain(int n) {
        var maxMtd = new Member(
            new TypeReference(typeof(Math).FullName!), nameof(Math.Max));
        Node c = new Constant(1);
        for (int i = 2; i <= n; i++)
            c = new Invoke(maxMtd, c, new Constant(i));
        return c;
    }

    private static Node BuildGcd(int a, int b) {
        var x = new Variable("x");
        var y = new Variable("y");
        var tmp = new Variable("tmp");
        return new Invoke(new Lambda([], new Block(
            [new Assignment(x, new Constant((long)a)),
             new Assignment(y, new Constant((long)b)),
             new WhileLoop(new NotEqual(y, new Constant(0L)),
                 new Block([
                     new Assignment(tmp, new Modulo(x, y)),
                     new Assignment(x, y),
                     new Assignment(y, tmp)
                 ])),
             x],
            [x, y, tmp])));
    }

    private static Node BuildSieve(int limit) {
        var wordCnt = (limit + 64) / 64;
        var bits = new Variable("bits");
        var i = new Variable("i"); var j = new Variable("j"); var cnt = new Variable("cnt");

        Node Wi(Node x) => new ShiftRight(x, new Constant(6));
        Node Bi(Node x) => new BitwiseAnd(x, new Constant(63L));
        Node Bit(Node x) => new ShiftLeft(new Constant(1L), Bi(x));
        Node IsP(Node x) => new Equal(new BitwiseAnd(
            new ShiftRight(new IndexAccess(bits, Wi(x)), Bi(x)), new Constant(1L)), new Constant(0L));

        return new Invoke(new Lambda([], new Block(
            [new Assignment(bits, new NewArray(TypeReference.To<long>(), new Constant(wordCnt))),
             new Assignment(i, new Constant(2)),
             new WhileLoop(new LessThanOrEqual(new Multiply(i, i), new Constant(limit)),
                 new Block([
                     new IfStatement(IsP(i),
                         new Block([
                             new Assignment(j, new Multiply(i, i)),
                             new WhileLoop(new LessThanOrEqual(j, new Constant(limit)),
                                 new Block([
                                     new Assignment(new IndexAccess(bits, Wi(j)),
                                         new BitwiseOr(new IndexAccess(bits, Wi(j)), Bit(j))),
                                     new Assignment(j, new Add(j, i))
                                 ]))
                         ])),
                     new Assignment(i, new Add(i, new Constant(1)))
                 ])),
             new Assignment(cnt, new Constant(0)),
             new Assignment(i, new Constant(2)),
             new WhileLoop(new LessThanOrEqual(i, new Constant(limit)),
                 new Block([
                     new Assignment(cnt, new Add(cnt, new Conditional(IsP(i),
                         new Constant(1), new Constant(0)))),
                     new Assignment(i, new Add(i, new Constant(1)))
                 ])),
             cnt],
            [bits, i, j, cnt])));
    }

    /// <summary>Mandelbrot escape-time count over a grid using
    /// fixed-point integer arithmetic (shift=8).  Exposes: tight
    /// mixed arithmetic loops (multiply, add, shift, compare),
    /// nested loops, and conditional escape checks.</summary>
    private static Node BuildMandelbrot(int size) {
        var x = new Variable("x"); var y = new Variable("y");
        var zx = new Variable("zx"); var zy = new Variable("zy");
        var zx2 = new Variable("zx2"); var zy2 = new Variable("zy2");
        var iter = new Variable("iter"); var total = new Variable("total");
        const int S = 8; // scale shift

        // Pixel center cx, cy as (x - size/2) * 4  scaled by S
        // So cx range is [-2*S*size/2, 2*S*size/2] which fits in long
        Node Cx(Node xv) => new ShiftLeft(
            new Multiply(new Subtract(new ShiftLeft(xv, new Constant(2)),
                new Constant(size)), new Constant(2)), new Constant(S));
        Node Cy(Node yv) => Cx(yv); // same formula

        // Inner Mandelbrot loop for one pixel
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

        return new Invoke(new Lambda([], new Block(
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
    }

    /// <summary>N-queens solver via iterative backtracking with an
    /// explicit stack.  Exposes: nested loops, bitwise operations,
    /// complex control flow, array access.</summary>
    private static Node BuildNQueens(int boardSize) {
        var stack = new Variable("stack");
        var sp = new Variable("sp");
        var total = new Variable("total");
        var ld = new Variable("ld"); var cols = new Variable("cols");
        var rd = new Variable("rd");
        var avail = new Variable("avail"); var bit = new Variable("bit");

        long allBits = (1L << boardSize) - 1;
        int maxDepth = boardSize;

        Node StackAt(Node idx) => new IndexAccess(stack, idx);
        Node Long(long v) => new Constant(v);

        int stackSize = maxDepth * maxDepth * maxDepth * 3;
        return new Invoke(new Lambda([], new Block(
            [new Assignment(stack, new NewArray(TypeReference.To<long>(), new Constant(stackSize))),
             new Assignment(sp, Long(0)),
             new Assignment(total, Long(0)),
             // push initial state: stack[sp]=0, stack[sp+1]=0, stack[sp+2]=0
             new Assignment(StackAt(sp), Long(0)),
             new Assignment(StackAt(new Add(sp, Long(1))), Long(0)),
             new Assignment(StackAt(new Add(sp, Long(2))), Long(0)),
             new Assignment(sp, new Add(sp, Long(3))),
             // while (sp > 0)
             new WhileLoop(new GreaterThan(sp, Long(0)), new Block([
                 // pop: sp -= 3; ld = stack[sp]; cols = stack[sp+1]; rd = stack[sp+2]
                 new Assignment(sp, new Subtract(sp, Long(3))),
                 new Assignment(ld, StackAt(sp)),
                 new Assignment(cols, StackAt(new Add(sp, Long(1)))),
                 new Assignment(rd, StackAt(new Add(sp, Long(2)))),
                 // if (cols == allBits) total++
                 new IfStatement(new Equal(cols, Long(allBits)),
                     new Assignment(total, new Add(total, Long(1)))),
                 // avail = ~(ld|cols|rd) & allBits
                 new Assignment(avail, new BitwiseAnd(
                     new BitwiseNot(new BitwiseOr(new BitwiseOr(ld, cols), rd)),
                     Long(allBits))),
                 // while (avail): push next state
                 new WhileLoop(new NotEqual(avail, Long(0)), new Block([
                     new Assignment(bit, new BitwiseAnd(new UnaryMinus(avail), avail)),
                     new Assignment(avail, new BitwiseXor(avail, bit)),
                     new Assignment(StackAt(sp),
                         new ShiftLeft(new BitwiseOr(ld, bit), Long(1))),
                     new Assignment(StackAt(new Add(sp, Long(1))),
                         new BitwiseOr(cols, bit)),
                     new Assignment(StackAt(new Add(sp, Long(2))),
                         new ShiftRight(new BitwiseOr(rd, bit), Long(1))),
                     new Assignment(sp, new Add(sp, Long(3))),
                 ])),
             ])),
             total],
            [stack, sp, total, ld, cols, rd, avail, bit])));
    }

    /// <summary>Collatz max sequence length for n in [1, limit].
    /// Exposes: mixed arithmetic (modulo, multiply, shift, add),
    /// variable-iteration while loops, unpredictable branching.</summary>
    private static Node BuildCollatz(int limit) {
        var n = new Variable("n"); var i = new Variable("i");
        var len = new Variable("len"); var maxLen = new Variable("maxLen");
        var bestN = new Variable("bestN");

        // for n = 1..limit:
        //   len = 1; i = n
        //   while (i != 1) { i = i % 2 == 0 ? i/2 : 3*i+1; len++; }
        //   if (len > maxLen) { maxLen = len; bestN = n; }
        return new Invoke(new Lambda([], new Block(
            [new Assignment(maxLen, new Constant(0L)),
             new Assignment(bestN, new Constant(0L)),
             new Assignment(n, new Constant(1L)),
             new WhileLoop(new LessThanOrEqual(n, new Constant(limit)),
                 new Block([
                     new Assignment(len, new Constant(1L)),
                     new Assignment(i, n),
                     new WhileLoop(new NotEqual(i, new Constant(1L)),
                         new Block([
                             new Assignment(i, new Conditional(
                                 new Equal(new Modulo(i, new Constant(2L)), new Constant(0L)),
                                 new ShiftRight(i, new Constant(1)),       // even: i/2
                                 new Add(new Multiply(i, new Constant(3L)), new Constant(1L)))),  // odd: 3i+1
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
             maxLen],
            [n, i, len, maxLen, bestN])));
    }

    private static Node BuildCountPrimes(int limit) {
        var n = new Variable("n");
        var i = new Variable("i");
        var count = new Variable("count");
        var isPrime = new Variable("isPrime");
        return new Invoke(new Lambda([], new Block(
            [new Assignment(count, new Constant(0L)),
             new Assignment(n, new Constant(2L)),
             new WhileLoop(new LessThanOrEqual(n, new Constant(limit)),
                 new Block([
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
                     // count += isPrime ? 1 : 0
                     new Assignment(count, new Add(count,
                         new Conditional(new Equal(isPrime, new Constant(1L)),
                             new Constant(1L), new Constant(0L)))),
                     new Assignment(n, new Add(n, new Constant(1L)))
                 ])),
             count],
            [n, i, count, isPrime])));
    }
}