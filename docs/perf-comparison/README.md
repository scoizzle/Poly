# Cross-language results: how to read them

`run.sh` runs each benchmark in `docs/perf-comparison/` across the languages it can find (host `dotnet`, and docker for C/C++/Rust/JS/Bun/Deno/Python), then writes `results/results_<timestamp>.csv` and a `.png`. Columns are `benchmark,language,size_or_limit,result,runs,min_us,max_us,avg_us,prep_ms`.

A Poly row's `prep_ms` is compile + analysis for that program, measured on its first run. Every other language reports `0` because they compile in `run.sh`, not in the timed program.

## The rows are not measured in one environment

Poly and the two C# entries run as **native arm64 .NET processes on the macOS host**. C, C++, Rust, JavaScript, Bun, Deno and Python run as **linux/arm64 images inside Docker Desktop**, compiled there (`gcc -O3 -march=native`, `rustc -O -C target-cpu=native`).

That boundary is not free, and its direction is not constant. The same C source, both arm64, measured through each path:

| Benchmark | clang 21 (host) | gcc 14 (docker) | CSV `C` row |
|---|---|---|---|
| nqueens 8 | 14-16 us | 8-9 us | 8.2 |
| collatz 1e6 | 106-113 ms | 151-160 ms | 154 |
| mandelbrot 128 | 825-1033 us | 793-815 us | 810 |
| sieve 1e6 | 3486-3835 us | 3725-4550 us | 3779 |

The C column moves by up to **1.8x** on toolchain and environment alone. So:

- **Ordering within the docker group is meaningful** - those rows share a compiler family and a container.
- **Poly versus a docker row is cross-environment.** "The VM beats C on collatz" is true against gcc-14-in-docker (154 ms vs 145 ms) and false against clang-on-host (107 ms vs 145 ms). Do not quote it as head-to-head.
- **The only like-for-like comparisons in the file are Poly versus `C# native` and `C# vectorized`**, which run on the same host through the same runtime.

Closing this properly means running the .NET side in a container too, or the native side on the host. Neither is done yet.

## Algorithm equivalence (checked 2026-10-23)

The four checksums match across every language - 78,498 / 458,080 / 92 / 837799:524 - which is the real evidence that each pair computes the same answer. Beyond that, the base implementations are algorithmically equivalent, with three asymmetries that a reader should know about:

| Benchmark | Verdict | Difference |
|---|---|---|
| nqueens | Equivalent | The bitmask iterative backtracking is line-for-line the same (same `avail`, same `avail & -avail`, same row-7 terminal). Poly uses `long[8]` where the rest use `int[8]`, and allocates the four arrays **inside** the timed region, plus wraps the body in a lambda invoke - both of which penalise Poly slightly. |
| sieve | Equivalent marking, **different counting** | Marking is identical bit-packed Eratosthenes; Poly expresses the inner mark loop as the shipped `StridedSetBits` intrinsic. Counting differs: every other language tests `limit` bits one at a time, Poly popcounts `wordCnt` words. Swapping only the count phase in C moved 1e6 from ~3,650 us to ~3,500 us, so this favours Poly by roughly 5-8%. |
| mandelbrot | Mathematically equivalent | Same threshold, same iteration semantics, same checksum. Poly recomputes `zx*zx` and `zy*zy` in the loop **guard** as well as the body, where the others compute them once into locals - so Poly does about twice the multiplies per iteration. This penalises Poly; its 1.32x gap to C is measured on the harder implementation. |
| collatz | Equivalent | Same naive walk with no memoisation. Poly shifts (`i >> 1`) where the others divide (`m / 2`) on a signed 64-bit type, where the compiler must emit sign correction for negatives. Making C use a shift changed 1e6 by under 1%; the asymmetry is real but negligible. |

`mandelbrot_polyvm_dbl.cs` and `mandelbrot_polyvm_double.cs` are **variants**, not the base algorithm: they map viewport coordinates differently, which is why their checksum is 453734 rather than 458080. Their rows are labelled with a suffix and are not a slowdown of the main row.

## Why nqueens is the weakest row (measured 2026-10-23)

The nqueens row is where Poly trails JavaScript on the best case (55 us vs 42 us), even though it beats JS on average. The cause is array element access, and it is nearly the whole runtime.

**Marginal cost of one array access inside the VM: 1.40 ns.** Measured by compiling two 100k-iteration loops through `Interpreter.Compile` - one reading a scalar, one reading `arr[i & 7]` - and differencing: scalar 28 us, array 169 us, delta 140 us. nqueens does roughly 36k element accesses per run, so ~50 us of its ~57 us is array access.

The emitted shape is why. Every `IndexAccess` emits a temporary local assigned `heap.UnsafeGet((int)handle)` (a call), a `castclass` to `long[]` (a runtime type check), then the element read. Hand-written equivalents of the same loop, in four shapes:

| Shape | min |
|---|---|
| deref + cast per access (what the VM emits) | 19.3 us |
| deref once per loop iteration | 12.7 us |
| deref once per run | 6.5 us |
| raw `int[]`, the shape C#/JS use | 5.3 us |

So the array representation costs up to 3.6x native before the VM's own per-access ceremony is counted, and the ceremony is what takes 19 us to 57 us.

**The fix is [#262](https://github.com/scoizzle/Poly/pull/262)**, which promotes a `new T[n]` local with a constant length in [1,16] and inline elements to a frame window (`_slots[_fp + base + i]`), removing the deref and the castclass. Measured on the same harness, same machine: **56-63 us before, 41-44 us after** (checksum 92 both sides) - about 27 % off, which puts Poly at JavaScript's best case instead of 30 % behind it.

Remaining headroom after #262: roughly 0.98 ns per access against a native ~0.2 ns, so about 35 us of the 41 us is still per-access ceremony - the temp local, an explicit window bounds check, and the CLR's own `_slots` bounds check. That is the next lever, not the heap deref.

This section measures the *VM*, not the cross-language table; the 1.40 ns figure is from the same machine on which the table's Poly rows were taken, and the native columns remain cross-environment (see above).

## What the table does and does not support

**It supports:** sustained-work throughput of the Poly VM against native and managed runtimes, and — because every language must print the same checksum — that the VM computes the same answer as C, C++, Rust and C# on these four programs. Within the host group, Poly versus `C# native` is the fair comparison.

**It does not support** any of the following, and a quote of the table should carry the caveat:

1. **Startup-sensitive latency.** NQueens@8 does ~60 µs of work and pays ~56 ms of `prep_ms`. The ratio is ~900x. The VM's speed is for work that runs long enough to amortise compile and analysis; a one-shot micro-program never does. Do not read a Poly row as "this is how fast it starts".
2. **Debug-mode throughput.** `Poly VM` is `CompilationMode.NoDebug`. `Poly VM (Normal)` is the same program with debug hooks and costs 14-62% more (collatz is the worst case: 145 ms -> 235 ms). Any Poly advantage is NoDebug only.
3. **Small-workload rows.** NQueens@8 is 40-60 µs across runs, where scheduler state and container CPU sharing dominate. Five runs is not enough to separate a real change from noise at that scale — a same-machine A/B against `9522d536` showed the current tree is *slightly faster* (61 -> 57 µs median) while the 2026-08-20 result in this directory reads 40 µs. Treat the nqueens column as the least trustworthy in the file.
4. **Variants as slowdowns.** Rows whose label carries a suffix (`Poly VM double (bits)`, `Poly VM double (grid)`) are *different programs* — `mandelbrot_polyvm_dbl.cs` and `mandelbrot_polyvm_double.cs` map coordinates differently, hence checksum 453734 instead of 458080. They are not the main row running slower.
5. **Cross-run trends.** Rows are only comparable within one CSV. Machine, container images and load differ between runs; compare two runs only with a same-machine A/B.
6. **Poly versus a containerized language, as a head-to-head.** See the section above: the two sides run in different environments and the native column moves by up to 1.8x between them.

## Method note

`Poly VM` and `Poly VM (Normal)` run the *same* source (`<bench>_polyvm.cs`) so the pair isolates the compilation mode. Any other `<bench>_polyvm_*.cs` is a variant and gets its own label. See `run_bench` in `run.sh`.
