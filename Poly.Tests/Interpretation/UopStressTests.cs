using System.Diagnostics;
using System.IO;

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
    private const int OneSecond = 1000;

    private static Bytecode Lower(Node node) {
        var builder = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator();
        return Lowering.Lower(node, builder.Build().Analyze(node));
    }

    [Test]
    public async Task CountPrimes_1M_SingleShot() {
        var sw = Stopwatch.StartNew();
        var prog = Lower(CountPrimesNode(1000000));
        using var state = new VmState { Program = prog };
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
        using var state = new VmState { Program = prog };
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
        using var state = new VmState { Program = prog };
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
        using var state = new VmState { Program = prog };
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
        using var state = new VmState { Program = prog };
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
        using var state = new VmState();
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
            [new Assignment(arr, new NewArray(new TypeReference("System.Int64"), new Constant(10))),
             new Assignment(new IndexAccess(arr, new Constant(5)), new Constant(42L)),
             new IndexAccess(arr, new Constant(5))],
            [arr]);
        var inv = new Invoke(new Lambda([], body));
        var analyzer = NodeTestHelpers.CreateTestAnalyzer();
        var prog = Lowering.Lower(inv, analyzer.Analyze(inv));
        // Dump µops
        var dump = string.Join("\n", prog.MicroOps.Select((m, i) => $"  [{i}] {m}"));
        File.AppendAllText("/tmp/poly_uop_dump.txt", dump + "\n");
        using var state = new VmState { Program = prog };
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
            [new Assignment(bits, new NewArray(new TypeReference("System.Int64"), new Constant(wordCnt))),
             new Assignment(i, new Constant(2)),
             new WhileLoop(new LessThanOrEqual(new Multiply(i, i), new Constant(limit)),
                 new Block([
                     new IfStatement(IsPrime(i),
                         new Block([
                             new Assignment(j, new Multiply(i, i)),
                             new WhileLoop(new LessThanOrEqual(j, new Constant(limit)),
                                 new Block([
                                     // tmp = bits[word] | bit  (read, then or)
                                     new Assignment(tmp, new BitwiseOr(
                                         new IndexAccess(bits, WordIdx(j)), Bit(j))),
                                     // bits[word] = tmp  (write without reading)
                                     new Assignment(new IndexAccess(bits, WordIdx(j)), tmp),
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
            [bits, tmp, i, j, cnt]);

        var inv = new Invoke(new Lambda([], body));
        var analyzer = NodeTestHelpers.CreateTestAnalyzer();
        var prog = Lowering.Lower(inv, analyzer.Analyze(inv));
        using var state = new VmState { Program = prog };
        state.Reset(); Vm.Execute(state);
        long result = (long)state.Stack.Pop();
        await Assert.That(result).IsEqualTo(9592L);
    }

    [Test]
    public async Task Sieve_1M_SingleShot() {
        var limit = 1000000;
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
            [new Assignment(bits, new NewArray(new TypeReference("System.Int64"), new Constant(wordCnt))),
             new Assignment(i, new Constant(2)),
             new WhileLoop(new LessThanOrEqual(new Multiply(i, i), new Constant(limit)),
                 new Block([
                     new IfStatement(IsPrime(i),
                         new Block([
                             new Assignment(j, new Multiply(i, i)),
                             new WhileLoop(new LessThanOrEqual(j, new Constant(limit)),
                                 new Block([
                                     // tmp = bits[word] | bit; bits[word] = tmp
                                     new Assignment(tmp, new BitwiseOr(
                                         new IndexAccess(bits, WordIdx(j)), Bit(j))),
                                     new Assignment(new IndexAccess(bits, WordIdx(j)), tmp),
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
            [bits, tmp, i, j, cnt]);

        var inv = new Invoke(new Lambda([], body));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var analyzer = NodeTestHelpers.CreateTestAnalyzer();
        var prog = Lowering.Lower(inv, analyzer.Analyze(inv));
        using var state = new VmState { Program = prog };
        state.Reset(); Vm.Execute(state); sw.Stop();
        long result = (long)state.Stack.Pop();
        File.AppendAllText("/tmp/poly_stress.txt",
            $"Sieve(1M) = {result} primes in {sw.ElapsedMilliseconds}ms\n");
        await Assert.That(result).IsEqualTo(78498L);
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