using BenchmarkDotNet.Attributes;

using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Syntax;
using Poly.Syntax.Nodes;

namespace Poly.Benchmarks;

/// <summary>
/// Measures PolyVM vs native C# performance for counting primes
/// via the Sieve of Eratosthenes at three scales:
///
///   Scale | Limit          | Algorithm     | π(limit) approx
///   ------|----------------|---------------|-----------------
///   1M    |      16,000,000| Flat sieve    |         1,031,130
///   10M   |     180,000,000| Flat sieve    |        10,017,595
///   1B    |  23,000,000,000| Segmented     |     1,006,163,969
///
/// All use bit-parallel packing (long[] as a bitset) and PopCount for
/// counting.  Both PolyVM and native run the same algorithm for each scale.
/// </summary>
[MemoryDiagnoser]
[MinColumn, MaxColumn, MeanColumn, MedianColumn]
public class PrimeSieveBenchmark {
    // ── Sieve limits ────────────────────────────────────────────
    private const int SieveLimit1M = 16_000_000;
    private const int SieveLimit10M = 180_000_000;
    private const long SieveLimit1B = 23_000_000_000L;

    // Segment size for the segmented sieve (2²⁰ = 1,048,576 numbers per segment).
    private const int SegmentSizeSegmented = 1 << 20;

    // ── Pre-compiled PolyVM programs ────────────────────────────
    private VmProgram? _polyVm1M;
    private VmProgram? _polyVm10M;
    private VmProgram? _polyVm1B;

    // Pre-allocated native bitsets (reused by Array.Clear).
    private long[]? _nativeBits1M;
    private long[]? _nativeBits10M;

    [GlobalSetup]
    public void Setup() {
        _polyVm1M = Interpreter.Compile(BuildSieveFlat(SieveLimit1M), CompilationMode.NoDebug);
        _polyVm10M = Interpreter.Compile(BuildSieveFlat(SieveLimit10M), CompilationMode.NoDebug);
        _polyVm1B = Interpreter.Compile(BuildSegmentedSieve(SieveLimit1B, SegmentSizeSegmented), CompilationMode.NoDebug);

        _nativeBits1M = new long[(SieveLimit1M + 64) / 64];
        _nativeBits10M = new long[(SieveLimit10M + 64) / 64];
    }

    // ═════════════════════════════════════════════════════════════
    //  Benchmarks
    // ═════════════════════════════════════════════════════════════

    [Benchmark(Baseline = true, Description = "Native C# / 1M (flat)")]
    public long Native_1M() => NativeFlatSieve(SieveLimit1M, _nativeBits1M!);

    [Benchmark(Description = "PolyVM / 1M (flat)")]
    public long PolyVm_1M() => RunVm(_polyVm1M!);

    [Benchmark(Description = "Native C# / 10M (flat)")]
    public long Native_10M() => NativeFlatSieve(SieveLimit10M, _nativeBits10M!);

    [Benchmark(Description = "PolyVM / 10M (flat)")]
    public long PolyVm_10M() => RunVm(_polyVm10M!, maxLoops: 1_000_000_000);

    [Benchmark(Description = "Native C# / 1B (segmented)")]
    public long Native_1B() => NativeSegmentedSieve(SieveLimit1B, SegmentSizeSegmented);

    [Benchmark(Description = "PolyVM / 1B (segmented)")]
    public long PolyVm_1B() => RunVm(_polyVm1B!, maxLoops: -1);

    // ═════════════════════════════════════════════════════════════
    //  Run helpers
    // ═════════════════════════════════════════════════════════════

    private static long RunVm(VmProgram program, long maxLoops = 200_000_000) {
        using var exec = Interpreter.Execute(program, s => s.MaxLoopIterations = maxLoops);
        return exec.RawValue;
    }

    // ═════════════════════════════════════════════════════════════
    //  Native C# — flat sieve (1M / 10M)
    // ═════════════════════════════════════════════════════════════

    private static long NativeFlatSieve(int limit, long[] bits) {
        int wordCnt = (limit + 64) / 64;
        Array.Clear(bits, 0, bits.Length);

        for (int i = 2; i * i <= limit; i++) {
            if ((bits[i >> 6] >> (i & 63) & 1) == 0) {
                for (int j = i * i; j <= limit; j += i)
                    bits[j >> 6] |= 1L << (j & 63);
            }
        }

        long count = 0;
        for (int w = 0; w < wordCnt - 1; w++)
            count += long.PopCount(~bits[w]);

        long lastMask = (limit & 63) == 63 ? -1L : (1L << ((limit & 63) + 1)) - 1L;
        if (lastMask < 0) lastMask = -1;
        count += long.PopCount(~bits[wordCnt - 1] & lastMask);

        return count - 2;
    }

    // ═════════════════════════════════════════════════════════════
    //  Native C# — segmented sieve (1B)
    // ═════════════════════════════════════════════════════════════

    private static long NativeSegmentedSieve(long limit, int segmentSize) {
        int sqrtLimit = (int)Math.Sqrt(limit);
        int sqrtWordCnt = (sqrtLimit + 64) / 64;
        long[] sqrtBits = new long[sqrtWordCnt];

        // Phase 1: simple sieve up to sqrt(limit) to find base primes
        for (int i = 2; i * i <= sqrtLimit; i++) {
            if ((sqrtBits[i >> 6] >> (i & 63) & 1) == 0) {
                for (int j = i * i; j <= sqrtLimit; j += i)
                    sqrtBits[j >> 6] |= 1L << (j & 63);
            }
        }

        // Extract base primes into a dense array
        int primeCount = 0;
        int[] basePrimes = new int[sqrtLimit / 8 + 1000];
        for (int i = 2; i <= sqrtLimit; i++) {
            if ((sqrtBits[i >> 6] >> (i & 63) & 1) == 0)
                basePrimes[primeCount++] = i;
        }

        long total = primeCount;
        int segWordCnt = (segmentSize + 64) / 64;
        long[] segment = new long[segWordCnt];

        // Phase 2: process each segment
        for (long low = sqrtLimit + 1; low <= limit; low += segmentSize) {
            long high = Math.Min(low + segmentSize - 1, limit);
            Array.Clear(segment, 0, segWordCnt);

            foreach (int pv in basePrimes.AsSpan(0, primeCount)) {
                long p = pv;
                long start = Math.Max(p * p, ((low + p - 1) / p) * p);
                if (start > high) continue;

                long relStart = start - low;
                long relLimit = high - low;
                for (long j = relStart; j <= relLimit; j += p)
                    segment[j >> 6] |= 1L << (int)(j & 63);
            }

            for (int w = 0; w < segWordCnt; w++)
                total += long.PopCount(~segment[w]);
        }

        // Subtract phantom primes at positions 0 and 1
        return total - 2;
    }

    // ═════════════════════════════════════════════════════════════
    //  PolyVM AST — flat sieve (1M / 10M)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Build a flat bit-parallel Sieve of Eratosthenes AST.
    /// Used for 1M and 10M (limits up to ~180M).
    /// </summary>
    private static Node BuildSieveFlat(int limit) {
        int wordCnt = (limit + 64) / 64;
        var bits = new Variable("bits");
        var i = new Variable("i");
        var cnt = new Variable("cnt");
        var w = new Variable("w");

        Node IsPrime(Node x) => new Equal(
            new BitwiseAnd(
                new ShiftRight(
                    new IndexAccess(bits, new ShiftRight(x, new Constant(6))),
                    new BitwiseAnd(x, new Constant(63L))),
                new Constant(1L)),
            new Constant(0L));

        long lastMask = (limit & 63) == 63 ? -1L : (1L << ((limit & 63) + 1)) - 1L;
        if (lastMask < 0) lastMask = -1;

        return new Block(
        [
            new Assignment(bits, new NewArray(TypeReference.To<long>(), new Constant(wordCnt))),
            new Assignment(i, new Constant(2)),
            new WhileLoop(
                new LessThanOrEqual(new Multiply(i, i), new Constant(limit)),
                new Block([
                    new IfStatement(IsPrime(i),
                        new StridedSetBits(bits, new Multiply(i, i), i, new Constant(limit))),
                    new Assignment(i, new Add(i, new Constant(1)))
                ])),
            new Assignment(cnt, new Constant(0L)),
            new Assignment(w, new Constant(0L)),
            new WhileLoop(
                new LessThan(w, new Constant(wordCnt - 1)),
                new Block([
                    new Assignment(cnt, new Add(cnt,
                        new PopCount(new BitwiseNot(new IndexAccess(bits, w))))),
                    new Assignment(w, new Add(w, new Constant(1L)))
                ])),
            new Assignment(cnt, new Add(cnt,
                new PopCount(new BitwiseAnd(
                    new BitwiseNot(new IndexAccess(bits, new Constant(wordCnt - 1))),
                    new Constant(lastMask))))),
            new Assignment(cnt, new Subtract(cnt, new Constant(2L))),
            cnt
        ], [bits, i, cnt, w]);
    }

    // ═════════════════════════════════════════════════════════════
    //  PolyVM AST — segmented sieve (1B)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Build a two-phase segmented Sieve of Eratosthenes AST.
    ///
    /// Phase 1 – sieve up to sqrt(limit) with a flat bit array and
    /// collect the base primes into a dense array.
    ///
    /// Phase 2 – walk the range [sqrtLimit+1 … limit] in segments.
    /// For each segment, allocate a fresh bit array (zeroed), cross off
    /// composites using every base prime via <see cref="StridedSetBits"/>,
    /// then count survivors with PopCount.
    /// </summary>
    private static Node BuildSegmentedSieve(long limit, int segmentSize) {
        int sqrtLimit = (int)Math.Sqrt(limit);
        int sqrtWordCnt = (sqrtLimit + 64) / 64;
        int segWordCnt = (segmentSize + 64) / 64;
        // Upper bound for π(sqrtLimit): 1.25506 · x / ln(x) rounded up.
        int maxPrimes = sqrtLimit / 8 + 2000;

        // ── Variables ───────────────────────────────────────────
        var baseBits = new Variable("baseBits");
        var basePrimes = new Variable("basePrimes");
        var seg = new Variable("seg");
        var i = new Variable("i");
        var p = new Variable("p");
        var low = new Variable("low");
        var high = new Variable("high");
        var total = new Variable("total");
        var pc = new Variable("pc");     // prime count
        var pi = new Variable("pi");     // prime index (loop variable)
        var w = new Variable("w");
        var s = new Variable("s");       // start
        var rs = new Variable("rs");     // relStart
        var rl = new Variable("rl");     // relLimit

        // IsPrime(bits, x) : ((bits[x>>6] >> (x&63)) & 1) == 0
        static Node IsPrime(Node bits, Node x) => new Equal(
            new BitwiseAnd(
                new ShiftRight(
                    new IndexAccess(bits, new ShiftRight(x, new Constant(6))),
                    new BitwiseAnd(x, new Constant(63L))),
                new Constant(1L)),
            new Constant(0L));

        var nodes = new List<Node>();

        // ════════════════════════════════════════════════════════
        //  Phase 1 – base primes up to sqrt(limit)
        // ════════════════════════════════════════════════════════
        nodes.Add(new Assignment(baseBits,
            new NewArray(TypeReference.To<long>(), new Constant(sqrtWordCnt))));
        nodes.Add(new Assignment(basePrimes,
            new NewArray(TypeReference.To<long>(), new Constant(maxPrimes))));

        // Sieve
        nodes.Add(new Assignment(i, new Constant(2L)));
        nodes.Add(new WhileLoop(
            new LessThanOrEqual(new Multiply(i, i), new Constant(sqrtLimit)),
            new Block([
                new IfStatement(IsPrime(baseBits, i),
                    new StridedSetBits(baseBits, new Multiply(i, i), i, new Constant(sqrtLimit))),
                new Assignment(i, new Add(i, new Constant(1L)))
            ])));

        // Extract base primes into basePrimes[]
        nodes.Add(new Assignment(pc, new Constant(0L)));
        nodes.Add(new Assignment(i, new Constant(2L)));
        nodes.Add(new WhileLoop(
            new LessThanOrEqual(i, new Constant(sqrtLimit)),
            new Block([
                new IfStatement(IsPrime(baseBits, i), new Block([
                    new Assignment(new IndexAccess(basePrimes, pc), i),
                    new Assignment(pc, new Add(pc, new Constant(1L)))
                ])),
                new Assignment(i, new Add(i, new Constant(1L)))
            ])));

        nodes.Add(new Assignment(total, pc));

        // ════════════════════════════════════════════════════════
        //  Phase 2 – segmented sieve
        // ════════════════════════════════════════════════════════
        nodes.Add(new Assignment(low, new Constant((long)sqrtLimit + 1)));

        nodes.Add(new WhileLoop(
            new LessThanOrEqual(low, new Constant(limit)),
            new Block([
                // Fresh segment array (CLR zero-initialises)
                new Assignment(seg,
                    new NewArray(TypeReference.To<long>(), new Constant(segWordCnt))),

                // high = min(low + segmentSize - 1, limit)
                new Assignment(high, new Conditional(
                    new LessThanOrEqual(
                        new Add(low, new Constant((long)(segmentSize - 1))),
                        new Constant(limit)),
                    new Add(low, new Constant((long)(segmentSize - 1))),
                    new Constant(limit))),

                // ── Mark composites with each base prime ──
                new Assignment(pi, new Constant(0L)),
                new WhileLoop(
                    new LessThan(pi, pc),
                    new Block([
                        new Assignment(p, new IndexAccess(basePrimes, pi)),

                        // start = max(p * p, ((low + p - 1) / p) * p)
                        new Assignment(s, new Conditional(
                            new GreaterThan(
                                new Multiply(p, p),
                                new Multiply(
                                    new Divide(
                                        new Add(low, new Subtract(p, new Constant(1L))),
                                        p),
                                    p)),
                            new Multiply(p, p),
                            new Multiply(
                                new Divide(
                                    new Add(low, new Subtract(p, new Constant(1L))),
                                    p),
                                p))),

                        new IfStatement(
                            new LessThanOrEqual(s, high),
                            new Block([
                                new Assignment(rs, new Subtract(s, low)),
                                new Assignment(rl, new Subtract(high, low)),
                                new StridedSetBits(seg, rs, p, rl)
                            ])),
                        new Assignment(pi, new Add(pi, new Constant(1L)))
                    ])),

                // ── Count survivors via PopCount ──
                new Assignment(w, new Constant(0L)),
                new WhileLoop(
                    new LessThan(w, new Constant(segWordCnt)),
                    new Block([
                        new Assignment(total, new Add(total,
                            new PopCount(new BitwiseNot(new IndexAccess(seg, w))))),
                        new Assignment(w, new Add(w, new Constant(1L)))
                    ])),

                new Assignment(low, new Add(low, new Constant((long)segmentSize)))
            ])));

        // Subtract 0 and 1 (not prime)
        nodes.Add(new Assignment(total, new Subtract(total, new Constant(2L))));
        nodes.Add(total);

        return new Block(nodes, [baseBits, basePrimes, seg, i, p, low, high,
            total, pc, pi, w, s, rs, rl]);
    }
}