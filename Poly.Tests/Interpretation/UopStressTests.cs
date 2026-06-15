using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.VirtualMachine;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>Throughput / stress tests.  Not correctness assertions —
/// measures how much work the µop pipeline can sustain per second.</summary>
public class UopStressTests {
    private static readonly TestTraceWriter? _trace = Debugger.IsAttached ? new() : null;
    private const int OneSecond = 1000;

    private static Bytecode Lower(Node node) {
        var result = new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .UseDefiniteAssignmentAnalysis()
            .Build()
            .Analyze(node);
        return Lowering.Lower(node, result);
    }

    [Test]
    public async Task CountPrimes_1M_SingleShot() {
        var sw = Stopwatch.StartNew();
        var prog = Lower(CountPrimesNode(1000000));
        using var state = new VmState { Program = prog, Trace = _trace };
        state.Reset();
        Vm.Execute(state);
        sw.Stop();
        long result = (long)Vm.Execute(state).Value!;
        var msg = $"CountPrimes(1000000) = {result} in {sw.ElapsedMilliseconds}ms";
        File.AppendAllText("/tmp/poly_stress.txt", msg + "\n");
        Console.Error.WriteLine(msg);
    }

    [Test]
    public async Task CountPrimes_Throughput_1s() {
        var prog = Lower(CountPrimesNode(100000));
        using var state = new VmState { Program = prog, Trace = _trace };
        state.Reset();
        var sw = Stopwatch.StartNew();
        int count = 0;
        while (sw.ElapsedMilliseconds < OneSecond) {
            state.Reset();
            Vm.Execute(state);
            count++;
        }
        sw.Stop();
        double usPerRun = (double)sw.ElapsedMilliseconds * 1000 / count;
        long result = (long)Vm.Execute(state).Value!;
        File.AppendAllText("/tmp/poly_stress.txt",
            $"CountPrimes(100000) = {result} ({usPerRun:F1} µs/run, {count} runs in {sw.ElapsedMilliseconds}ms)\n");
    }

    [Test]
    public async Task LoopSum_Throughput_1s() {
        var prog = Lower(LoopSumNode(100000));
        using var state = new VmState { Program = prog, Trace = _trace };
        state.Reset();
        var sw = Stopwatch.StartNew();
        int count = 0;
        while (sw.ElapsedMilliseconds < OneSecond) {
            state.Reset();
            Vm.Execute(state);
            count++;
        }
        sw.Stop();
        double usPerRun = (double)sw.ElapsedMilliseconds * 1000 / count;
        long result = (long)Vm.Execute(state).Value!;
        File.AppendAllText("/tmp/poly_stress.txt",
            $"LoopSum(100000) = {result} ({usPerRun:F1} µs/run, {count} runs in {sw.ElapsedMilliseconds}ms)\n");
    }

    [Test]
    public async Task Fibonacci_Throughput_1s() {
        var prog = Lower(FibNode(30));
        using var state = new VmState { Program = prog, Trace = _trace };
        state.Reset();
        var sw = Stopwatch.StartNew();
        int count = 0;
        while (sw.ElapsedMilliseconds < OneSecond) {
            state.Reset();
            Vm.Execute(state);
            count++;
        }
        sw.Stop();
        double usPerRun = (double)sw.ElapsedMilliseconds * 1000 / count;
        long result = (long)Vm.Execute(state).Value!;
        File.AppendAllText("/tmp/poly_stress.txt",
            $"Fib(30) = {result} ({usPerRun:F1} µs/run, {count} runs in {sw.ElapsedMilliseconds}ms)\n");
    }

    [Test]
    public async Task Gcd_Throughput_1s() {
        var prog = Lower(GcdNode(12345678, 98765432));
        using var state = new VmState { Program = prog, Trace = _trace };
        state.Reset();
        var sw = Stopwatch.StartNew();
        int count = 0;
        while (sw.ElapsedMilliseconds < OneSecond) {
            state.Reset();
            Vm.Execute(state);
            count++;
        }
        sw.Stop();
        double usPerRun = (double)sw.ElapsedMilliseconds * 1000 / count;
        long result = (long)Vm.Execute(state).Value!;
        File.AppendAllText("/tmp/poly_stress.txt",
            $"Gcd(12345678, 98765432) = {result} ({usPerRun:F1} µs/run, {count} runs in {sw.ElapsedMilliseconds}ms)\n");
    }

    // ── Builders ──

    private static Node CountPrimesNode(int limit) {
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
                             new Assignment(isPrime, new Conditional(
                                 new Equal(new Modulo(n, i), new Constant(0L)),
                                 new Constant(0L), isPrime)),
                             new Assignment(i, new Add(i, new Constant(1L)))
                         ])),
                     new Assignment(count, new Add(count,
                         new Conditional(new Equal(isPrime, new Constant(1L)),
                             new Constant(1L), new Constant(0L)))),
                     new Assignment(n, new Add(n, new Constant(1L)))
                 ])),
             count],
            [n, i, count, isPrime])));
    }

    private static Node LoopSumNode(int n) {
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

    private static Node FibNode(int n) {
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

    [Test]
    public async Task Array_BasicReadWrite() {
        // Pure µop-level test: NewArrayOp + ArrayStoreOp + ArrayLoadOp round-trip
        using var state = new VmState { Trace = _trace };
        state.Stack.Push(10);
        var compiled = ProgramCompiler.Compile(new MicroOp[] {
            new NewArrayOp(), new DupOp(), new PushOp(5L),
            new PushOp(42L), new ArrayStoreOp(), new PushOp(5L),
            new ArrayLoadOp(),
        });
        compiled(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(42L);
    }

    [Test]
    public async Task Array_ThroughLowering() {
        // Full lowering path: new long[10]; arr[5] = 42; return arr[5]
        var arr = new Variable("arr");
        var body = new Block(
            [new Assignment(arr, new NewArray(TypeReference.To<long>(), new Constant(10))),
             new Assignment(new IndexAccess(arr, new Constant(5)), new Constant(42L)),
             new IndexAccess(arr, new Constant(5))],
            [arr]);
        var inv = new Invoke(new Lambda([], body));
        var prog = Lowering.Lower(inv, inv.AnalyzeNode());
        // Dump µops
        var dump = string.Join("\n", prog.MicroOps.Select((m, i) => $"  [{i}] {m}"));
        File.AppendAllText("/tmp/poly_uop_dump.txt", dump + "\n");
        using var state = new VmState { Program = prog, Trace = _trace };
        state.Reset(); Vm.Execute(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(42L);
    }

    [Test]
    public async Task Sieve_100000_Compare() {
        var limit = 100000;
        var wordCnt = (limit + 64) / 64;
        var bits = new Variable("bits"); var tmp = new Variable("tmp");
        var i = new Variable("i"); var j = new Variable("j"); var cnt = new Variable("cnt");

        Node WordIdx(Node idx) => new ShiftRight(idx, new Constant(6));
        Node BitIdx(Node idx) => new BitwiseAnd(idx, new Constant(63L));
        Node Bit(Node idx) => new ShiftLeft(new Constant(1L), BitIdx(idx));
        Node IsPrime(Node idx) => new Equal(
            new BitwiseAnd(new ShiftRight(new IndexAccess(bits, WordIdx(idx)), BitIdx(idx)), new Constant(1L)),
            new Constant(0L));

        var body = new Block(
            [new Assignment(bits, new NewArray(TypeReference.To<long>(), new Constant(wordCnt))),
             new Assignment(i, new Constant(2)),
             new WhileLoop(new LessThanOrEqual(new Multiply(i, i), new Constant(limit)),
                 new Block([
                     new IfStatement(IsPrime(i),
                         new Block([
                             new Assignment(j, new Multiply(i, i)),
                              new WhileLoop(new LessThanOrEqual(j, new Constant(limit)),
                                  new Block([
                                      // direct: bits[word] |= bit  (StridedSetOp handles read+write)
                                      new Assignment(new IndexAccess(bits, WordIdx(j)),
                                          new BitwiseOr(new IndexAccess(bits, WordIdx(j)), Bit(j))),
                                      new Assignment(j, new Add(j, i))
                                  ]))
                         ])),
                     new Assignment(i, new Add(i, new Constant(1)))
                 ])),
             new Assignment(cnt, new Constant(0)),
             new Assignment(i, new Constant(2)),
             new WhileLoop(new LessThanOrEqual(i, new Constant(limit)),
                 new Block([
                     new Assignment(cnt, new Add(cnt, new Conditional(IsPrime(i),
                         new Constant(1), new Constant(0)))),
                     new Assignment(i, new Add(i, new Constant(1)))
                 ])),
             cnt],
            [bits, i, j, cnt]);

        var inv = new Invoke(new Lambda([], body));
        var prog = Lowering.Lower(inv, inv.AnalyzeNode());
        using var state = new VmState { Program = prog, Trace = _trace };
        state.Reset(); Vm.Execute(state);
        long result = (long)state.Stack.Pop();
        await Assert.That(result).IsEqualTo(9592L);
    }

    [Test]
    public async Task Sieve_1M_SingleShot() {
        var limit = 1000000;
        var wordCnt = (limit + 64) / 64;
        var bits = new Variable("bits");
        var i = new Variable("i"); var j = new Variable("j"); var cnt = new Variable("cnt");

        Node WordIdx(Node idx) => new ShiftRight(idx, new Constant(6));
        Node BitIdx(Node idx) => new BitwiseAnd(idx, new Constant(63L));
        Node Bit(Node idx) => new ShiftLeft(new Constant(1L), BitIdx(idx));
        Node IsPrime(Node idx) => new Equal(
            new BitwiseAnd(new ShiftRight(new IndexAccess(bits, WordIdx(idx)), BitIdx(idx)), new Constant(1L)),
            new Constant(0L));

        var body = new Block(
            [new Assignment(bits, new NewArray(TypeReference.To<long>(), new Constant(wordCnt))),
             new Assignment(i, new Constant(2)),
             new WhileLoop(new LessThanOrEqual(new Multiply(i, i), new Constant(limit)),
                 new Block([
                     new IfStatement(IsPrime(i),
                         new Block([
                             new Assignment(j, new Multiply(i, i)),
                             new WhileLoop(new LessThanOrEqual(j, new Constant(limit)),
                                  new Block([
                                      new Assignment(new IndexAccess(bits, WordIdx(j)),
                                          new BitwiseOr(new IndexAccess(bits, WordIdx(j)), Bit(j))),
                                      new Assignment(j, new Add(j, i))
                                  ]))
                         ])),
                     new Assignment(i, new Add(i, new Constant(1)))
                 ])),
             new Assignment(cnt, new Constant(0)),
             new Assignment(i, new Constant(2)),
             new WhileLoop(new LessThanOrEqual(i, new Constant(limit)),
                 new Block([
                     new Assignment(cnt, new Add(cnt, new Conditional(IsPrime(i),
                         new Constant(1), new Constant(0)))),
                     new Assignment(i, new Add(i, new Constant(1)))
                 ])),
             cnt],
            [bits, i, j, cnt]);

        var inv = new Invoke(new Lambda([], body));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var prog = Lowering.Lower(inv, inv.AnalyzeNode());
        sw.Stop();
        {

            using var file = File.OpenWrite("/tmp/poly_stress.txt");
            using var writer = new StreamWriter(file);
            prog.Dump(writer);
        }
        sw.Start();
        using var state = new VmState { Program = prog, Trace = _trace };
        state.Reset();
        Vm.Execute(state);
        sw.Stop();
        long result = (long)state.Stack.Pop();
        File.AppendAllText("/tmp/poly_stress.txt",
            $"Sieve(1M) = {result} primes in {sw.ElapsedMilliseconds}ms\n");
        await Assert.That(result).IsEqualTo(78498L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  BatchReduceOp — word-level reduce over long[]
    // ═══════════════════════════════════════════════════════════════

    private static MicroOp[] Once(ushort words, long val, int idx) => [
        new NewArrayOp(),
        new DupOp(),
        new PushOp(idx), new PushOp(val), new ArrayStoreOp(),
    ];

    private static async Task AssertReduce(long expected, long init, ushort words, MicroOp[] setup,
        Func<Expression, Expression, Expression> reducer) {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(words);
        var uops = new List<MicroOp>();
        uops.AddRange(setup);
        uops.Add(new PushOp(words));
        uops.Add(new PushOp(init));
        uops.Add(new BatchReduceOp(reducer));
        ProgramCompiler.Compile(uops.ToArray())(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(expected);
    }

    // ── Sum ──

    [Test]
    public async Task BatchSum_Empty_ReturnsInitial() {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(1);
        var c = ProgramCompiler.Compile([
            new NewArrayOp(), new PushOp(1), new PushOp(99L), new BatchReduceOp(BatchReduceOp.Sum)]);
        c(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(99L);
    }

    [Test]
    public async Task BatchSum_SingleWord_SumsElements() {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(3);
        ProgramCompiler.Compile([
            new NewArrayOp(), new DupOp(),
            new PushOp(0L), new PushOp(10L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(1L), new PushOp(20L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(2L), new PushOp(30L), new ArrayStoreOp(),
            new PushOp(3), new PushOp(0L), new BatchReduceOp(BatchReduceOp.Sum),
        ])(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(60L);
    }

    [Test]
    public async Task BatchSum_WithInitialState() {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(2);
        ProgramCompiler.Compile([
            new NewArrayOp(), new DupOp(),
            new PushOp(0L), new PushOp(100L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(1L), new PushOp(200L), new ArrayStoreOp(),
            new PushOp(2), new PushOp(50L), new BatchReduceOp(BatchReduceOp.Sum),
        ])(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(350L);
    }

    // ── CountNonZero ──

    [Test]
    public async Task BatchCountNonZero_AllZero_ReturnsZero() {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(10);
        ProgramCompiler.Compile([
            new NewArrayOp(), new PushOp(10), new PushOp(0L), new BatchReduceOp(BatchReduceOp.CountNonZero),
        ])(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(0L);
    }

    [Test]
    public async Task BatchCountNonZero_MixedElements() {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(3);
        ProgramCompiler.Compile([
            new NewArrayOp(), new DupOp(),
            new PushOp(0L), new PushOp(0L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(1L), new PushOp(42L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(2L), new PushOp(0L), new ArrayStoreOp(),
            new PushOp(3), new PushOp(0L), new BatchReduceOp(BatchReduceOp.CountNonZero),
        ])(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(1L);
    }

    // ── BitwiseOr ──

    [Test]
    public async Task BatchBitwiseOr_Accumulates() {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(3);
        ProgramCompiler.Compile([
            new NewArrayOp(), new DupOp(),
            new PushOp(0L), new PushOp(0b001L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(1L), new PushOp(0b010L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(2L), new PushOp(0b100L), new ArrayStoreOp(),
            new PushOp(3), new PushOp(0L), new BatchReduceOp(BatchReduceOp.BitwiseOr),
        ])(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(0b111L);
    }

    // ── BitwiseAnd ──

    [Test]
    public async Task BatchBitwiseAnd_Accumulates() {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(3);
        ProgramCompiler.Compile([
            new NewArrayOp(), new DupOp(),
            new PushOp(0L), new PushOp(0b111L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(1L), new PushOp(0b110L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(2L), new PushOp(0b101L), new ArrayStoreOp(),
            new PushOp(3), new PushOp(0b111L), new BatchReduceOp(BatchReduceOp.BitwiseAnd),
        ])(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(0b100L);
    }

    // ── Min / Max ──

    [Test]
    public async Task BatchMin_FindsMinimum() {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(3);
        ProgramCompiler.Compile([
            new NewArrayOp(), new DupOp(),
            new PushOp(0L), new PushOp(50L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(1L), new PushOp(10L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(2L), new PushOp(30L), new ArrayStoreOp(),
            new PushOp(3), new PushOp(long.MaxValue), new BatchReduceOp(BatchReduceOp.Min),
        ])(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(10L);
    }

    [Test]
    public async Task BatchMax_FindsMaximum() {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(3);
        ProgramCompiler.Compile([
            new NewArrayOp(), new DupOp(),
            new PushOp(0L), new PushOp(50L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(1L), new PushOp(10L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(2L), new PushOp(30L), new ArrayStoreOp(),
            new PushOp(3), new PushOp(long.MinValue), new BatchReduceOp(BatchReduceOp.Max),
        ])(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(50L);
    }

    // ── Negative values ──

    [Test]
    public async Task BatchSum_Negatives() {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(3);
        ProgramCompiler.Compile([
            new NewArrayOp(), new DupOp(),
            new PushOp(0L), new PushOp(-5L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(1L), new PushOp(10L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(2L), new PushOp(-3L), new ArrayStoreOp(),
            new PushOp(3), new PushOp(0L), new BatchReduceOp(BatchReduceOp.Sum),
        ])(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(2L);
    }

    // ── Large word count ──

    [Test]
    public async Task BatchSum_100Words() {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(100);
        var uops = new List<MicroOp> { new NewArrayOp() };
        for (int w = 0; w < 100; w++) {
            uops.Add(new DupOp());
            uops.Add(new PushOp(w));
            uops.Add(new PushOp(1L));
            uops.Add(new ArrayStoreOp());
        }
        uops.Add(new PushOp(100));
        uops.Add(new PushOp(0L));
        uops.Add(new BatchReduceOp(BatchReduceOp.Sum));
        ProgramCompiler.Compile(uops.ToArray())(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(100L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  CountBitsOp
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task CountBits_SingleWord_ThreeBits() {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(5);
        ProgramCompiler.Compile([
            new NewArrayOp(), new DupOp(),
            new PushOp(0L), new PushOp(42L), new ArrayStoreOp(), // bits[0]=42 → 3 bits
            new PushOp(5), new CountBitsOp(),
        ])(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(3L);
    }

    [Test]
    public async Task CountBits_Empty_ReturnsZero() {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(10);
        ProgramCompiler.Compile([
            new NewArrayOp(), new PushOp(10), new CountBitsOp(),
        ])(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(0L);
    }

    [Test]
    public async Task CountBits_AllOnes_Returns64() {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(3);
        ProgramCompiler.Compile([
            new NewArrayOp(), new DupOp(),
            new PushOp(0L), new PushOp(-1L), new ArrayStoreOp(),
            new PushOp(3), new CountBitsOp(),
        ])(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(64L);
    }

    [Test]
    public async Task CountBits_MultipleWords_Accumulates() {
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(3);
        ProgramCompiler.Compile([
            new NewArrayOp(), new DupOp(),
            new PushOp(0L), new PushOp(-1L), new ArrayStoreOp(),
            new DupOp(),
            new PushOp(1L), new PushOp(1L), new ArrayStoreOp(),
            new PushOp(3), new CountBitsOp(),
        ])(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(65L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  StridedSetOp
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task StridedSet_SingleBit_SetsOne() {
        // Direct test: create array, call StridedSetOp, read back array[0]
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(1);
        // Stack: [1] → NewArrayOp → [handle]
        // Then: push 2(start), 1(step), 2(limit) → [handle, 2, 1, 2]
        // StridedSetOp pops limit=2, step=1, start=2, handle → marks, leaves stack empty
        // Then: push 0, ArrayLoadOp → reads arr[0] → 4 (bit 2 set)
        var compiled = ProgramCompiler.Compile([
            new NewArrayOp(),
            new PushOp(2L), new PushOp(1L), new PushOp(2L),
            new StridedSetOp(),
            new PushOp(0L), new ArrayLoadOp(),
        ]);
        compiled(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(4L);
    }

    [Test]
    public async Task StridedSet_MultipleSteps_SingleWord() {
        // Mark j=2,4,6 (step=2, limit=6) — single word, 3 bits set
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(1);
        ProgramCompiler.Compile([
            new NewArrayOp(),
            new PushOp(2L), new PushOp(2L), new PushOp(6L),
            new StridedSetOp(),           // [handle] (not consumed)
            new PushOp(1),                 // wordCount
            new CountBitsOp(),
        ])(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(3L);
    }

    [Test]
    public async Task StridedSet_MultipleWords() {
        // Mark j=60..70 step=1 — spans words 0 and 1, 11 bits set
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(2);
        ProgramCompiler.Compile([
            new NewArrayOp(),
            new PushOp(60L), new PushOp(1L), new PushOp(70L),
            new StridedSetOp(),
            new PushOp(2),                  // wordCount = 2
            new CountBitsOp(),
        ])(s);
        await Assert.That(s.Stack.Pop()).IsEqualTo(11L);
    }

    [Test]
    public async Task StridedSet_MarksComposites() {
        // Full sieve up to 100K using only StridedSetOp
        // Compare result with known prime count: 9592 primes ≤ 100000
        var limit = 100000;
        var wordCnt = (limit + 64) / 64;
        using var s = new VmState { Trace = _trace };
        s.Stack.Push(wordCnt);
        var uops = new List<MicroOp> { new NewArrayOp() };
        // Sieve: for i = 2; i*i <= limit; i++
        //   if bit i not set → for j = i*i; j <= limit; j += i → set bit
        // StridedSetOp uses Top() for handle (doesn't consume it),
        // so DupOp is not needed — handle stays on stack from NewArrayOp.
        for (int i = 2; i * i <= limit; i++) {
            uops.Add(new PushOp((long)(i * i)));   // start
            uops.Add(new PushOp((long)i));          // step
            uops.Add(new PushOp((long)limit));      // limit
            uops.Add(new StridedSetOp());
        }
        uops.Add(new PushOp(wordCnt));
        uops.Add(new CountBitsOp());
        ProgramCompiler.Compile(uops.ToArray())(s);
        long composites = s.Stack.Pop();
        long primes = limit - 1 - composites; // 2..limit inclusive
        await Assert.That(primes).IsEqualTo(9592L);
    }

    private static Node GcdNode(int a, int b) {
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
}