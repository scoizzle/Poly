using BenchmarkDotNet.Attributes;

using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Interpretation.Vm;

namespace Poly.Benchmarks;

/// <summary>
/// Program-level VM timings for the four workloads the interpretation-simplification
/// perf gates name — nqueens, sieve, mandelbrot, collatz. Each program is read from the
/// C# surface and compiled once in <see cref="GlobalSetup"/>; the benchmark executes the
/// compiled program and returns its checksum, so no work can be elided. The program
/// texts are frozen in docs/plans/interpretation-simplification/tasks/ISB.md.
/// </summary>
[MemoryDiagnoser]
[MinColumn, MaxColumn, MeanColumn, MedianColumn]
public class ProgramBenchmarks {
    private const string NQueensSource = """
        class NQueens {
            NQueens() { }
            long Place(long row, long n, long cols, long diag, long anti, long full) {
                if (row == n) { return 1; }
                var count = 0L;
                var avail = full & ~(cols | diag | anti);
                while (avail != 0) {
                    var bit = avail & (0 - avail);
                    avail = avail - bit;
                    count = count + this.Place(row + 1, n, cols | bit, (diag | bit) << 1, (anti | bit) >> 1, full);
                }
                return count;
            }
            long Run(long n) {
                var full = (1L << n) - 1;
                return this.Place(0, n, 0, 0, 0, full);
            }
        }
        new NQueens().Run(9);
        """;

    private const string SieveSource = """
        class Sieve {
            Sieve() { }
            long Run(long limit) {
                var words = limit / 64 + 1;
                var bits = new long[words];
                for (var i = 2; i * i <= limit; i = i + 1) {
                    if (((bits[i >> 6] >> (i & 63)) & 1L) == 0) {
                        for (var j = i * i; j <= limit; j = j + i) {
                            bits[j >> 6] = bits[j >> 6] | (1L << (j & 63));
                        }
                    }
                }
                var composite = 0L;
                for (var w = 0; w < words; w = w + 1) {
                    var v = bits[w];
                    for (var b = 0; b < 64; b = b + 1) {
                        composite = composite + (v & 1L);
                        v = v >> 1;
                    }
                }
                return limit - 1 - composite;
            }
        }
        new Sieve().Run(20000);
        """;

    private const string MandelbrotSource = """
        class Mandelbrot {
            Mandelbrot() { }
            long Run(long side, long maxIter) {
                var total = 0L;
                for (var y = 0; y < side; y = y + 1) {
                    for (var x = 0; x < side; x = x + 1) {
                        var cr = (double)x / (double)side * 2.5 - 2.0;
                        var ci = (double)y / (double)side * 2.0 - 1.0;
                        var zr = 0.0;
                        var zi = 0.0;
                        var iter = 0L;
                        while (iter < maxIter) {
                            var zr2 = zr * zr;
                            var zi2 = zi * zi;
                            if (zr2 + zi2 > 4.0) { break; }
                            zi = 2.0 * zr * zi + ci;
                            zr = zr2 - zi2 + cr;
                            iter = iter + 1;
                        }
                        total = total + iter;
                    }
                }
                return total;
            }
        }
        new Mandelbrot().Run(40, 50);
        """;

    private const string CollatzSource = """
        class Collatz {
            Collatz() { }
            long Run(long limit) {
                var total = 0L;
                for (var i = 1; i <= limit; i = i + 1) {
                    var m = i;
                    while (m != 1) {
                        if (m % 2 == 0) { m = m / 2; } else { m = 3 * m + 1; }
                        total = total + 1;
                    }
                }
                return total;
            }
        }
        new Collatz().Run(3000);
        """;

    private VmProgram _nQueens = null!;
    private VmProgram _sieve = null!;
    private VmProgram _mandelbrot = null!;
    private VmProgram _collatz = null!;

    [GlobalSetup]
    public void Setup() {
        _nQueens = Compile(NQueensSource);
        _sieve = Compile(SieveSource);
        _mandelbrot = Compile(MandelbrotSource);
        _collatz = Compile(CollatzSource);
        Verify(_nQueens, 352);
        Verify(_sieve, 2262);
        Verify(_mandelbrot, 32828);
        Verify(_collatz, 215063);
    }

    [Benchmark]
    public long NQueens() => Run(_nQueens);

    [Benchmark]
    public long Sieve() => Run(_sieve);

    [Benchmark]
    public long Mandelbrot() => Run(_mandelbrot);

    [Benchmark]
    public long Collatz() => Run(_collatz);

    private static long Run(VmProgram program) {
        using var execution = Interpreter.Execute(program);
        return execution.RawValue;
    }

    private static VmProgram Compile(string source) {
        var unit = CSharpLanguage.Read(source);
        var entry = unit.TopLevelStatements is [var statement]
            ? statement
            : throw new InvalidOperationException("a benchmark program is one top-level statement");
        return Interpreter.Compile(entry, Interpreter.Analyze(unit), CompilationMode.NoDebug);
    }

    private static void Verify(VmProgram program, long expected) {
        var actual = Run(program);
        if (actual != expected)
            throw new InvalidOperationException($"benchmark program computed {actual}, expected {expected}");
    }
}