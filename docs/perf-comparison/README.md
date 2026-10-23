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

**Landed 2026-10-23 (IS7d):** the array object is now cached in a typed CLR local at its definition, so an element access is one bounds check and nothing else. Array nqueens **57/63 us -> 39-42/40-42** (min/median), and the Sieve program dropped **105.6 -> 61.5 us (-42 %)**. Zero heap derefs remain in either emitted tree. Design and the corrected comparison with the frame window: [`docs/analysis/array-access-representation.md`](../analysis/array-access-representation.md).

This section measures the *VM*, not the cross-language table; the 1.40 ns figure is from the same machine on which the table's Poly rows were taken, and the native columns remain cross-environment (see above).

## Final run on `plan/interpretation-simplification` (2026-10-23 15:17)

`results/results_20261023_151722.csv`, exec avg (min) against the same-environment baselines:

| Benchmark | Poly VM | C# native | Ratio | JS |
|---|---:|---:|---:|---:|
| nqueens 8 | 56 us (41) | 31 | 1.81x | 46 |
| sieve 1e6 | 3,701 us (3,570) | 3,869 | **0.96x** | 48,065 |
| mandelbrot 128 | 1,056 us (984) | 789 | 1.34x | 2,514 |
| collatz 1e6 | 138,695 us (135,920) | 170,164 | **0.82x** | 2,051,328 |

Poly is ahead of C# native on sieve and collatz, and ahead of JavaScript on every program by min. Nqueens is the one place the average loses to JS (56 vs 46) because of a single 70 us outlier - its min (41) is at JS's 43.

**Change against the 07:08 run on the same machine:**

| Workload | 07:08 | 15:17 | |
|---|---:|---:|---|
| `Poly VM double (bits)` | 2,226 | **1,353** | **-39 %** |
| `Poly VM double (grid)` | 2,218 | **1,297** | **-42 %** |
| nqueens Poly VM (min) | 55 | **41** | -25 % |
| collatz Poly VM | 145,124 | 138,695 | -4.4 % |
| mandelbrot Poly VM | 1,069 | 1,056 | -1.2 % |
| sieve Poly VM | 3,431 | 3,701 | **+7.9 %** |

The double variants are the typed-slot work landing (IS7a/IS7b); nqueens is the array cache (IS7d).

**Sieve is a real small regression, now bisected.** Two independent runs agree (3,570 and 3,574 min) while the C row held steady (3,748 vs 3,779) in the same runs, so the machine is comparable. Same machine, 12 interleaved samples per tree:

| Tree | min | median |
|---|---:|---:|
| `b4798ad2` - before IS7a/b | 3,314 | 3,506 |
| `644d1b9f` - after IS7c | 3,563 | 3,683 |
| `7cfdde81` - `master` | 3,379 | 3,591 |

So IS7a/IS7b/IS7c cost ~5 %, and later slices recovered about half of it.

**The mechanism, read off the emitted body** ([`sieve-expression-body.txt`](sieve-expression-body.txt)): the only structural difference between the pre-IS7 tree and the current one is that the shift amount `6` is now an **Int32 ring slot** (`_i4`) rather than an Int64 slot behind a `Convert`. Both materialise the constant into a local; the new form drops a conversion but adds a second local to the frame. There is no algorithmic change.

**The larger cost in this program was older and unrelated, and is now fixed (IS7h).** The strided mark loop used to re-dereference the heap **twice per iteration**, because `bits[j >> 6] |= mask` lowers to `a[i] = a[i] | mask` and each `a[i]` re-dereferenced and re-cast - `StridedSetBits` received the array as an *argument*, so the array cache declined to promote it. Treating the intrinsic's array operand as a receiver (it is never handed on, only indexed) promotes it: the loop is now `_arr0[i] | mask` with zero derefs, and `sieve_polyvm.cs` goes **3,379 -> 2,995 us min** (-11 %), which also erases the regression recorded above.

## The call frame (2026-10-23)

Nqueens is call-heavy and the other three are not, so the call frame was measured on
its own. Counting executed statements through `VmState.DebugHook` (counts only — the
`Normal` hook changes the emitted code, so it cannot time anything) gives 8,393
`Place` invocations in a nqueens(9) run of 66,096 statement visits. A micro-probe — two
programs differing by exactly one operation, differenced over 400k iterations — priced
one call at **8.5 ns marginal**, against 0.25-0.75 ns for every other operation
measured (add, multiply, divide, double multiply, array read, array write, if/else).

Two changes came out of that, each kept only after an interleaved A/B:

| Change | Why | nqueens |
|---|---|---|
| Frame words passed as parameters, not a `long[]` | the call allocated an array per invocation; the protocol never needed one | **-20 %** |
| Restore is catch-and-rethrow, not `try`/`finally` | a `finally` is a funclet **call** on the normal path | **-36 %** total |

The per-call marginal cost went 8.5 -> 4.75 ns. Sieve, mandelbrot and collatz are flat
within noise, as expected for programs with one or two call sites.

Two things were measured and **rejected**: copying the frame words through
`slots.AsSpan(callSp, n)` instead of a bounded loop (one bounds check instead of *n*,
but a `memmove` call costs more than a handful of stores: 124.3/126.5 us median against
123.9/124.5), and a within-process A/B that rewrote the emitted tree back to the array
form — it reuses one GC heap, so the array variant's allocation was still collected
during the direct variant's runs and hid most of the win. Both shapes must be measured
in separate processes.

Machine note: these runs shared a host at load average ~5 (another user), so absolute
microseconds drift between sessions by up to 40 %. Every figure above is a ratio from
an interleaved A/B on one binary, not a cross-session comparison.

### Should every function body be inlined into one expression?

The next architectural lever is to stop emitting each AST function as its own
`Action<VmState>` and instead put every body in one expression, so a call is argument
slots plus a jump to a label and a `return` is a jump back. That removes the delegate
dispatch and the per-frame prologue, which is the bulk of what is left in the 4.75 ns.

Two facts were checked before answering:

- **Expression trees permit it.** A `Return`/`Goto` from inside a `TryCatch` or
  `TryFinally` to a label in an enclosing block is accepted — both validate *and* compile
  and run. So `try` around a call does not block the design. (C# the *language* forbids
  jumping into a block, so the emitted form must keep every label at the top level, or be
  a flat pc dispatch; that is a shape constraint, not a blocker.)
- **The shape is genuinely faster, in native code.** The same nqueens(9) kernel, three
  ways, median of 41 runs, two passes:

  | Shape | min | median |
  |---|---:|---:|
  | recursive C# methods (floor for "a call is a call") | 80.7 us | 81.0 us |
  | one method, calls are a pc jump — the proposal | 64.6 us | 67.6 us |
  | explicit stack, no calls at all | 24.7 us | 25.5 us |

  The jump shape is **16.5 % faster than recursion** on native code, where a call is
  already as cheap as a call gets. So the idea is sound.

**Why it was not done here.** Two independent estimates agree on the prize and it is
smaller than the change: the frame protocol is 8,393 calls x 4.75 ns = 40 us of a 128 us
run (31 %), but the proposal keeps the slot writes and the FP save and only removes
dispatch and prologue — recovery is roughly 16-21 % on nqueens, and **zero on the other
three programs**, which have one or two call sites. The two changes above already took
nqueens 182 -> 129 us (-29 %). Against that, inlining all bodies into one lambda has to
re-derive what the frame protocol gives for free: recursive bodies can no longer share
one set of `_regN` locals (they would collide across overlapping invocations, so every
local becomes a frame-slot spill), `try` regions nest differently, closure capture and
the per-statement debug hook all change, and one giant lambda risks the JIT's size budget
and instruction cache where a per-function delegate does not. That is a board-level
change to the ABI, not a slice.

**And the shape is not where the remaining time is.** The iterative column is 3.2x faster
than recursion for the same answer. The largest wins left in this kernel are in what the
emitter chooses to compute per call — re-deriving masks instead of carrying `tried` per
depth — not in how the call gets there.

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
