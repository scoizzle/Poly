# Cross-language results: how to read them

`run.sh` runs each benchmark in `docs/perf-comparison/` across the languages it can find (host `dotnet`, and docker for C/C++/Rust/JS/Bun/Deno/Python), then writes `results/results_<timestamp>.csv` and a `.png`. Columns are `benchmark,language,size_or_limit,result,runs,min_us,max_us,avg_us,prep_ms`.

A Poly row's `prep_ms` is compile + analysis for that program, measured on its first run. Every other language reports `0` because they compile in `run.sh`, not in the timed program.

## What the table does and does not support

**It supports:** sustained-work throughput of the Poly VM against native and managed runtimes, and — because every language must print the same checksum — that the VM computes the same answer as C, C++, Rust and C# on these four programs.

**It does not support** any of the following, and a quote of the table should carry the caveat:

1. **Startup-sensitive latency.** NQueens@8 does ~60 µs of work and pays ~56 ms of `prep_ms`. The ratio is ~900x. The VM's speed is for work that runs long enough to amortise compile and analysis; a one-shot micro-program never does. Do not read a Poly row as "this is how fast it starts".
2. **Debug-mode throughput.** `Poly VM` is `CompilationMode.NoDebug`. `Poly VM (Normal)` is the same program with debug hooks and costs 14-62% more (collatz is the worst case: 145 ms -> 235 ms). The "beats C" results are NoDebug only.
3. **Small-workload rows.** NQueens@8 is 40-60 µs across runs, where scheduler state and container CPU sharing dominate. Five runs is not enough to separate a real change from noise at that scale — a same-machine A/B against `9522d536` showed the current tree is *slightly faster* (61 -> 57 µs median) while the 2026-08-20 result in this directory reads 40 µs. Treat the nqueens column as the least trustworthy in the file.
4. **Variants as slowdowns.** Rows whose label carries a suffix (`Poly VM double (bits)`, `Poly VM double (grid)`) are *different programs* — `mandelbrot_polyvm_dbl.cs` and `mandelbrot_polyvm_double.cs` map coordinates differently, hence checksum 453734 instead of 458080. They are not the main row running slower.
5. **Cross-run trends.** Rows are only comparable within one CSV. Machine, container images and load differ between runs; compare two runs only with a same-machine A/B.

## Method note

`Poly VM` and `Poly VM (Normal)` run the *same* source (`<bench>_polyvm.cs`) so the pair isolates the compilation mode. Any other `<bench>_polyvm_*.cs` is a variant and gets its own label. See `run_bench` in `run.sh`.
