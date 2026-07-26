using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Real-world tests using the Sieve of Eratosthenes algorithm.
/// Exercises NewArray, StridedSetBits, PopCount, bitwise ops, nested loops,
/// and IndexAccess in a realistic composite benchmark.
/// </summary>
public class InterpretationSieveTests {
    /// <summary>
    /// Build a Sieve of Eratosthenes AST that counts primes up to <paramref name="limit"/>.
    /// </summary>
    private static Node BuildSieve(int limit) {
        int wordCnt = (limit + 64) / 64;
        var bits = new Variable("bits");
        var i = new Variable("i");
        var cnt = new Variable("cnt");
        var w = new Variable("w");

        // IsPrime(x): ((bits[x >> 6] >> (x & 63)) & 1) == 0
        Node IsPrime(Node x) => new Equal(
            new BitwiseAnd(
                new ShiftRight(
                    new IndexAccess(bits,
                        new ShiftRight(x, new Constant(6))),
                    new BitwiseAnd(x, new Constant(63L))),
                new Constant(1L)),
            new Constant(0L));

        // Last-word mask: mask out bits beyond limit
        long lastMask = (limit & 63) == 63 ? -1L : (1L << ((limit & 63) + 1)) - 1L;
        if (lastMask < 0) lastMask = -1;

        return new Block(
        [
            // bits = new long[wordCnt]
            new Assignment(bits, new NewArray(TypeReference.To<long>(), new Constant(wordCnt))),
            // for (int i = 2; i * i <= limit; i++)
            new Assignment(i, new Constant(2)),
            new WhileLoop(
                new LessThanOrEqual(new Multiply(i, i), new Constant(limit)),
                new Block([
                    new IfStatement(IsPrime(i),
                        new Block([
                            // Cross off multiples: for (j = i*i; j <= limit; j += i) bits[j >> 6] |= 1L << (j & 63)
                            new StridedSetBits(bits,
                                new Multiply(i, i),  // start = i*i
                                i,                    // step = i
                                new Constant(limit))  // limit = limit
                        ])),
                    new Assignment(i, new Add(i, new Constant(1)))
                ])),
            // Count primes: PopCount(~bits[w]) for each full word
            new Assignment(cnt, new Constant(0L)),
            new Assignment(w, new Constant(0L)),
            new WhileLoop(
                new LessThan(w, new Constant(wordCnt - 1)),
                new Block([
                    new Assignment(cnt, new Add(cnt,
                        new PopCount(new BitwiseNot(new IndexAccess(bits, w))))),
                    new Assignment(w, new Add(w, new Constant(1L)))
                ])),
            // Last word: mask then PopCount
            new Assignment(cnt, new Add(cnt,
                new PopCount(new BitwiseAnd(
                    new BitwiseNot(new IndexAccess(bits, new Constant(wordCnt - 1))),
                    new Constant(lastMask))))),
            // Subtract phantom primes at positions 0 and 1
            new Assignment(cnt, new Subtract(cnt, new Constant(2L))),
            cnt
        ], [bits, i, cnt, w]);
    }

    /// <summary>Compile a node in NoDebug mode with generous loop limits.</summary>
    private static VmProgram CompileSieve(Node node) =>
        Interpreter.Compile(node, CompilationMode.NoDebug);

    /// <summary>Execute a pre-compiled sieve program and return the prime count.</summary>
    private static long RunSieve(VmProgram program) =>
        Interpreter.Execute(program, s => s.MaxLoopIterations = 100_000_000).RawValue;

    // ═══════════════════════════════════════════════════════════════
    // Known prime counts (π(x)) for verification
    // ═══════════════════════════════════════════════════════════════

    [Test, Timeout(10_000)]
    public async Task Sieve_UpTo10_CountsPrimes(CancellationToken ct) {
        // π(10) = 4  (2, 3, 5, 7)
        var program = CompileSieve(BuildSieve(10));
        await Assert.That(RunSieve(program)).IsEqualTo(4L);
    }

    [Test, Timeout(10_000)]
    public async Task Sieve_UpTo100_CountsPrimes(CancellationToken ct) {
        // π(100) = 25
        var program = CompileSieve(BuildSieve(100));
        await Assert.That(RunSieve(program)).IsEqualTo(25L);
    }

    [Test, Timeout(10_000)]
    public async Task Sieve_UpTo1000_CountsPrimes(CancellationToken ct) {
        // π(1000) = 168
        var program = CompileSieve(BuildSieve(1000));
        await Assert.That(RunSieve(program)).IsEqualTo(168L);
    }

    [Test, Timeout(10_000)]
    public async Task Sieve_UpTo10000_CountsPrimes(CancellationToken ct) {
        // π(10000) = 1229
        var program = CompileSieve(BuildSieve(10000));
        await Assert.That(RunSieve(program)).IsEqualTo(1229L);
    }

    [Test, Timeout(10_000)]
    public async Task StridedSetBits_Direct_Test(CancellationToken ct) {
        // Minimal test: allocate a long[2], set bits 4,6,8 via StridedSetBits,
        // read back via IndexAccess.
        var arr = new Variable("arr");
        var idx = new Variable("idx");
        var body = new Block([
            new Assignment(arr, new NewArray(TypeReference.To<long>(), new Constant(2))),
            // Set bits 4,6,8 (start=4, step=2, limit=8) in word 0
            new StridedSetBits(arr, new Constant(4L), new Constant(2L), new Constant(8L)),
            // Read word 0: should have bits 4,6,8 set => value = (1<<4)|(1<<6)|(1<<8) = 336
            new IndexAccess(arr, new Constant(0))
        ], [arr]);

        var program = Interpreter.Compile(body, CompilationMode.NoDebug);
        long result = Interpreter.Execute(program, s => s.MaxLoopIterations = 100_000).RawValue;
        await Assert.That(result).IsEqualTo(336L);
    }

    [Test, Timeout(10_000)]
    public async Task StridedSetBits_Dynamic_Step_Variable(CancellationToken ct) {
        // Like the sieve: step = i (a variable), start = i*i (computed from variable)
        var arr = new Variable("arr");
        var i = new Variable("i");
        var body = new Block([
            new Assignment(arr, new NewArray(TypeReference.To<long>(), new Constant(2))),
            new Assignment(i, new Constant(2L)),
            new StridedSetBits(arr, new Multiply(i, i), i, new Constant(10L)),
            // After setting bits 4,6,8,10: word 0 should have bits 4,6,8,10 set
            new IndexAccess(arr, new Constant(0))
        ], [arr, i]);

        var program = Interpreter.Compile(body, CompilationMode.NoDebug);
        long result = Interpreter.Execute(program, s => s.MaxLoopIterations = 100_000).RawValue;
        // Bits 4,6,8,10 set => (1<<4)|(1<<6)|(1<<8)|(1<<10) = 16+64+256+1024 = 1360
        await Assert.That(result).IsEqualTo(1360L);
    }

    [Test, Timeout(10_000)]
    public async Task StridedSetBits_InIfStatement_LikeSieve(CancellationToken ct) {
        var arr = new Variable("arr");
        var i = new Variable("i");
        Node IsPrime(Variable iv) => new Equal(
            new BitwiseAnd(
                new ShiftRight(new IndexAccess(arr,
                    new ShiftRight(iv, new Constant(6))),
                    new BitwiseAnd(iv, new Constant(63L))),
                new Constant(1L)),
            new Constant(0L));

        var body = new Block([
            new Assignment(arr, new NewArray(TypeReference.To<long>(), new Constant(2))),
            new Assignment(i, new Constant(2L)),
            new IfStatement(IsPrime(i),
                new StridedSetBits(arr, new Multiply(i, i), i, new Constant(10L))),
            new IndexAccess(arr, new Constant(0))
        ], [arr, i]);

        var program = Interpreter.Compile(body, CompilationMode.NoDebug);
        long result = Interpreter.Execute(program, s => s.MaxLoopIterations = 100_000).RawValue;
        await Assert.That(result).IsEqualTo(1360L);
    }

    [Test, Timeout(10_000)]
    public async Task WhileLoop_Counter_Increments_And_Exits(CancellationToken ct) {
        // Minimal while loop test: i=0; while(i<5) { i=i+1; } result = i
        var i = new Variable("i");
        var body = new Block([
            new Assignment(i, new Constant(0L)),
            new WhileLoop(
                new LessThan(i, new Constant(5L)),
                new Assignment(i, new Add(i, new Constant(1L)))),
            i
        ], [i]);

        var program = Interpreter.Compile(body, CompilationMode.NoDebug);
        long result = Interpreter.Execute(program, s => s.MaxLoopIterations = 100_000).RawValue;
        await Assert.That(result).IsEqualTo(5L);
    }

    [Test, Timeout(10_000)]
    public async Task WhileLoop_LessThanOrEqual_WithMultiply_Condition(CancellationToken ct) {
        // i=2; while(i*i <= 100) { i = i+1; } — should stop when i=11 (11*11=121 > 100)
        var i = new Variable("i");
        var body = new Block([
            new Assignment(i, new Constant(2L)),
            new WhileLoop(
                new LessThanOrEqual(new Multiply(i, i), new Constant(100L)),
                new Assignment(i, new Add(i, new Constant(1L)))),
            i
        ], [i]);

        var program = Interpreter.Compile(body, CompilationMode.NoDebug);
        long result = Interpreter.Execute(program, s => s.MaxLoopIterations = 100_000).RawValue;
        // i should be 11 (first value where i*i > 100)
        await Assert.That(result).IsEqualTo(11L);
    }

    [Test, Timeout(10_000)]
    public async Task Sieve_Full_RingDepth_Test(CancellationToken ct) {
        // Full sieve pattern but with prime counting via simple AND, not PopCount
        var bits = new Variable("bits");
        var i = new Variable("i");
        var cnt = new Variable("cnt");
        var w = new Variable("w");
        int limit = 10;
        int wordCnt = (limit + 64) / 64;

        Node IsPrime(Variable iv) => new Equal(
            new BitwiseAnd(
                new ShiftRight(new IndexAccess(bits,
                    new ShiftRight(iv, new Constant(6))),
                    new BitwiseAnd(iv, new Constant(63L))),
                new Constant(1L)),
            new Constant(0L));

        var body = new Block([
            new Assignment(bits, new NewArray(TypeReference.To<long>(), new Constant(wordCnt))),
            new Assignment(i, new Constant(2L)),
            new WhileLoop(
                new LessThanOrEqual(new Multiply(i, i), new Constant(limit)),
                new Block([
                    new IfStatement(IsPrime(i),
                        new StridedSetBits(bits, new Multiply(i, i), i, new Constant(limit))),
                    new Assignment(i, new Add(i, new Constant(1L)))
                ])),
            // Count by iterating i from 2 to limit and checking IsPrime
            new Assignment(cnt, new Constant(0L)),
            new Assignment(i, new Constant(2L)),
            new WhileLoop(
                new LessThanOrEqual(i, new Constant(limit)),
                new Block([
                    new IfStatement(IsPrime(i),
                        new Assignment(cnt, new Add(cnt, new Constant(1L)))),
                    new Assignment(i, new Add(i, new Constant(1L)))
                ])),
            cnt
        ], [bits, i, cnt, w]);

        var program = Interpreter.Compile(body, CompilationMode.NoDebug);
        long result = Interpreter.Execute(program, s => s.MaxLoopIterations = 100_000).RawValue;
        await Assert.That(result).IsEqualTo(4L);  // π(10) = 4
    }
}