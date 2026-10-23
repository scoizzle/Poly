# TASK ISB - a checked-in benchmark baseline for IS3 and IS5

Status: implemented. Branch `slice/ISB`. Lane C (`vm`). No dependency; must merge before IS3.
Plan: [IS-README](../IS-README.md). Audit §3 and §5 ("it must be measured on the benchmark harness").

## Why

The audit's perf gates for IS3 (`CompileNode` totality) and IS5 (invoke unification) cite "the `/tmp/polybench` recipe, nqueens/sieve/mandelbrot/collatz". **That recipe is not in the repository** — `grep -rn polybench` finds nothing. `Poly.Benchmarks` has `PrimeSieveBenchmark`, `SimulatorInvokeBenchmark`, and `FunctionCalling`, and none of them is a program-level VM timing for the four shapes the audit names.

A perf gate an agent cannot reproduce is not a gate. This card makes the baseline something a cold reader can run on the tree it is comparing.

## Scope

- Add `Poly.Benchmarks/ProgramBenchmarks.cs` — four `[Benchmark]` methods, each a complete program compiled and executed through the production path (`Interpreter.Compile` + `Interpreter.Execute`) and returning or checksumming its result so the JIT cannot elide it:
  - `NQueens` — backtracking search with a recursion/lambda path (exercises invoke and frame setup);
  - `Sieve` — the shapes `PrimeSieveBenchmark` already uses, at a size that runs in milliseconds;
  - `Mandelbrot` — floating-point arithmetic and nested loops;
  - `Collatz` — tight loop with a branch and integer arithmetic.
- The program bodies are frozen by this card. Paste the exact source into the task file at implementation time (one short block per program) so a later slice cannot change the workload and call the change a speedup.
- Keep BenchmarkDotNet as the runner; no new harness package, no `/tmp` recipes, no custom stopwatch loop in the PR description.
- Record the baseline in this card's Log: the exact command and `Min`/`Median` per benchmark, on the commit IS3 will branch from.

## Files

- New: `Poly.Benchmarks/ProgramBenchmarks.cs`
- `Poly.Benchmarks/README.md` — if one exists, add the run line; do not create a README for this alone (see the audit's research limits on consumers)

`Poly.Benchmarks/README.md` does not exist, so none was created; the run line lives in §Frozen programs.

## Shape matrix

| Item | Before | After | Test that proves it |
|---|---|---|---|
| the four programs | not in the repo | `[Benchmark]` methods | `dotnet run -c Release --project Poly.Benchmarks -- --list flat` shows them |
| a baseline | a `/tmp` recipe | a Log row with the command and numbers | the Log |
| comparability | unknown workload | frozen bodies in this card | the card |

## Frozen programs

`Poly.Benchmarks/ProgramBenchmarks.cs` carries these four texts verbatim. Each is read with `CSharpLanguage.Read`, analyzed once with `Interpreter.Analyze`, compiled with `Interpreter.Compile(entry, analysis, CompilationMode.NoDebug)`, and executed with `Interpreter.Execute`. `[GlobalSetup]` runs each program once and fails unless it returns the noted checksum, so a changed workload cannot pass as a speedup.

Run them (BenchmarkDotNet `ShortRun`: 3 warmup + 3 measured iterations):

```
dotnet run -c Release --project Poly.Benchmarks -- --filter '*ProgramBenchmarks*' --job short
```

### NQueens — 9-queens placement count, 352

```csharp
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
```

### Sieve — primes below 20,000, 2,262

Bit-packed `long[]` marking as `PrimeSieveBenchmark` does. The source surface has no popcount, so the count pass shifts each word 64 times (fixed count: a `while (v != 0)` would not terminate on a word whose top bit is set, because `>>` sign-extends).

```csharp
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
```

### Mandelbrot — escape-iteration sum over a 40x50 grid, 32,828

```csharp
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
```

### Collatz — total stopping steps for 1...3000, 215,063

```csharp
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
```

## Done when

- `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` is clean (0 warnings; the project treats warnings as errors).
- The four benchmarks list and run.
- The baseline row exists with `Min` and `Median` per benchmark, plus the commit SHA it was measured on.
- No production file changed.

## SHIP if / NOT SHIP if

SHIP: the baseline is reproducible from the repo with one command, and the workloads are frozen in the card.
NOT SHIP: a benchmark measures something other than the compiled program; the numbers were taken on a different workload than the one committed; a custom timing loop replaced the runner.

## Tests

```
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run -c Release --project Poly.Benchmarks -- --list flat
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmTreeInvariantTests/*'
```

## Hand-edit

Four short program bodies in one file, each with the run command written above it.

## Needs Scot

none.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-22 | agent | Copilot/VS Code | `e0426fa5` | SHIP — four program-level benchmarks on `Interpreter.Compile` + `Interpreter.Execute`; the workloads are frozen above and checksum-verified in `GlobalSetup` | `grep -rn polybench` returns nothing, so the recipe IS3/IS5 cite never existed here; the baseline below is the replacement. `--job short` (3 iterations) is not a gate: two runs of the same tree differed by up to 2.3x on NQueens/Mandelbrot, so the recorded numbers are `--job medium` and IS3/IS5 must compare on the same job. Build 0 warnings; `--list flat` shows all four; full suite 4314/4314; `VmTreeInvariantTests` 6/6. The C# surface could not express the obvious shapes (see Findings), so the programs are written to what executes today; that is a measurement constraint, not a production change. |

### Baseline (2026-10-22, `e0426fa5`)

```
$ dotnet run -c Release --project Poly.Benchmarks -- --filter '*ProgramBenchmarks*' --job medium
```

`Job=MediumRun  IterationCount=15  LaunchCount=2  WarmupCount=10`
Apple M3 Pro, 12 logical/12 physical cores; .NET 10.0.12, Arm64 RyuJIT; SDK 10.0.401.

| Method | Min | Median | Mean | Allocated |
|--------|-----|--------|------|-----------|
| NQueens | 165.9 us | 166.7 us | 169.4 us | 671,992 B |
| Sieve | 157.1 us | 158.7 us | 158.9 us | 3,000 B |
| Mandelbrot | 136.0 us | 140.1 us | 149.6 us | 480 B |
| Collatz | 132.6 us | 141.2 us | 141.4 us | 472 B |

`e0426fa5` is the commit that carries `Poly.Benchmarks/ProgramBenchmarks.cs`; the commit recording this row touches only this card, so the numbers describe the branch tip as well.

The numbers are execution only: `GlobalSetup` reads, analyzes, and compiles once, and each benchmark runs the compiled `VmProgram`. `CompilationMode.NoDebug` omits the debug hook and the loop-tick guard, as `PrimeSieveBenchmark` does. The first `--job short` run on the same tree read 167.5/167.6, 160.2/161.4, 142.5/145.5, 137.1/139.2 (Min/Median) — consistent with the row above; the second short run read 171.6/380.6, 244.3/245.1, 142.3/315.8, 141.4/149.0, which is why the gate needs the medium job.

### Diagnostics (2026-10-22)

```
$ dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
    0 Warning(s), 0 Error(s)

$ dotnet run -c Release --project Poly.Benchmarks -- --list flat   # the four, among the existing benchmarks
Poly.Benchmarks.ProgramBenchmarks.NQueens
Poly.Benchmarks.ProgramBenchmarks.Sieve
Poly.Benchmarks.ProgramBenchmarks.Mandelbrot
Poly.Benchmarks.ProgramBenchmarks.Collatz

$ dotnet run --project Poly.Tests/Poly.Tests.csproj
  total: 4314 / failed: 0 / succeeded: 4314

$ dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmTreeInvariantTests/*'
  total: 6 / failed: 0 / succeeded: 6
```

### Findings from freezing the workloads

None of these is fixed here (no production file changes); they are what the source surface and the VM currently do, and each one shaped a program above.

1. **The `/tmp/polybench` recipe the IS3/IS5 gates cite does not exist.** `grep -rn polybench` finds nothing in the tree or its history; this card's row is the only baseline.
2. **A `CompilationUnitNode` is not compilable.** `Interpreter.Compile(unit, mode)` throws `AbiEmitter: unsupported node type CompilationUnitNode`, and analyzing the wrapped block instead of the unit loses the type declarations (`no matching constructor for 'C' with 0 argument(s)`). A program with a type declaration compiles only as `Interpreter.Compile(entry, Interpreter.Analyze(unit), mode)` — which is what `ProgramBenchmarks.Compile` does, and what `CSharpScript.Run` does internally.
3. **Recursion works through `this`; nothing else in the invoke path does.** `this.F(n - 1)` resolves, so `NQueens` and a self-recursive factorial probe return their answers. But: a call to a **sibling** method whose result is assigned to a local fails (`cannot assign an unresolved value to variable 't'`), a **recursive method with an array parameter** fails at analysis (``Type 'Dictionary`2' does not define method 'Place' with 2 parameter(s)``) — which is why NQueens uses three bitmask longs instead of arrays — a **local function** is `Variable 'Add' is not declared`, an unqualified `Add(...)`/`N(...)` is the same, a **static method** cannot be reached (`no matching constructor`), and a **void method** call fails with the same ``Dictionary`2`` message. A self-recursive lambda (`var f = (long n) => … f(n - 1)`) does not fail closed: it **stack-overflows the analyzer** in `TypeAndMemberResolver.ResolveMethodInvocationType`.
4. **A typed array local does not analyze.** `long[] a = new long[4];` → `VM compile rejected: TypeCast target type is unresolved`; `var a = new long[4];` works. The programs use `var` throughout for that reason.
5. **`var count = 0;` is Int32.** Mixing it with long arithmetic fails closed (`cannot assign 'Int64' to variable 'count' (incompatible with prior 'Int32')`), so every long accumulator is seeded `0L`.
6. **`>>` sign-extends in the VM.** A `while (v != 0) { … v = v >> 1; }` bit count never terminates on a word whose top bit is set (it reached the 500M loop cap in 285 s). `Sieve` counts with a fixed 64-iteration pass instead, and its `while (avail != 0)` loop stays on masks that cannot go negative.
7. **`--job short` is not reproducible enough to gate on.** Same tree, two runs: NQueens Median 167.6 us vs 380.6 us. The baseline above is `--job medium`; IS3 and IS5 should record `Min`/`Median` with the same job and the same machine class.
