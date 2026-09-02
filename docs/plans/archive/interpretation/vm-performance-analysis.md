# VM Performance Analysis

## Current Benchmarks

Baseline measurements on Apple M3 Pro, .NET 10 (from `Poly.Benchmarks/InterpreterBenchmarks.cs`):

| Method | Mean | Error | StdDev | Gen0 | Gen1 | Allocated |
|--------|------|-------|--------|------|------|-----------|
| Baseline_Poly | 1.576 ns | 0.0339 ns | 0.0301 ns | — | — | — |
| Vm_Poly | 71.610 ns | 0.5339 ns | 0.4994 ns | 0.1558 | 0.0005 | 1304 B |
| LinqJit_Poly | 2.902 ns | 0.0308 ns | 0.0273 ns | 0.0029 | — | 24 B |
| LinqInterp_Poly | 119.982 ns | 1.7453 ns | 1.6325 ns | 0.0496 | — | 416 B |
| Vm_PolyParam | — | — | — | — | — | — |
| LinqJit_PolyParam | — | similar to above | — | — | — | — |
| LinqInterp_PolyParam | — | — | — | — | — | — |
| Vm_ClrSimple | 107.500 ns | 0.8687 ns | 0.8126 ns | 0.1558 | 0.0005 | 1304 B |
| Vm_ClrChain | 2,551.194 ns | 15.6351 ns | 14.6251 ns | 0.1907 | — | 1600 B |
| Vm_Nested100 | 71.519 ns | 0.8127 ns | 0.7602 ns | 0.1558 | 0.0005 | 1304 B |

The VM is ~45× slower than raw C# for pure arithmetic. Per-execution `VmState` allocation has been eliminated (now 0 B for dispatch-only benchmarks). Remaining 1304 B includes return-value boxing, heap constant pre-load, and BDN harness overhead.

## Microbenchmark Suite

`Poly.Benchmarks/Microbenchmarks.cs` — 12 targeted microbenchmarks for individual VM components, run via:

```bash
dotnet run --project Poly.Benchmarks/Poly.Benchmarks.csproj -c Release -- --micro-bench
```

| Benchmark | Mean | Allocated | What it measures |
|-----------|------|-----------|-----------------|
| `PushPopInt` | — | — | Single `Push(42)` + `PopInt()` round-trip |
| `PushPopInt_Deep` | — | — | 100 push + 100 pop sequential |
| `PushPopTwo` | — | — | `PushTwo` + `PopTwo` tuple round-trip |
| `HeapAllocGet` | — | — | `Allocate(42)` + `Get(h)` round-trip |
| `HeapAllocSet` | — | — | `Allocate(0)` + `Set(h, 42)` |
| `HeapAllocFreeReuse` | — | — | Allocate → Set null → re-allocate |
| `Dispatch_10Nops` | 343 ns | **0 B** | VM execution of 10 Nop instructions |
| `Dispatch_100Nops` | 976 ns | **0 B** | VM execution of 100 Nop instructions |
| `Call_NoArgs` | 7,112 ns | 504 B | Function call + return round-trip |
| `Closure_SingleCapture` | 5,935 ns | 408 B | AllocateClosure + CallClosure |
| `Optimize_SimpleArith` | — | — | Peephole optimizer on small program |
| `Optimize_DeepNested` | — | — | Peephole optimizer on 100-level deep program |

> Dispatch benchmarks confirm `VmState.Reset()` eliminates the 312 B/op allocation — dispatch now measures **0 B** allocated.
> Call and Closure benchmarks still show allocation from frame setup, closure heap objects, and return-value boxing.

## Microbenchmark Suite

`Poly.Benchmarks/Microbenchmarks.cs` — 10 targeted microbenchmarks for individual VM components, run via:

```bash
dotnet run --project Poly.Benchmarks/Poly.Benchmarks.csproj -c Release -- --micro-bench
```

| Benchmark | What it measures |
|-----------|-----------------|
| `PushPopInt` | Single `Push(42)` + `PopInt()` round-trip |
| `PushPopInt_Deep` | 100 push + 100 pop sequential |
| `PushPopTwo` | `PushTwo(10,20)` + `PopTwo()` tuple round-trip |
| `HeapAllocGet` | `Allocate(42)` + `Get(h)` round-trip |
| `HeapAllocSet` | `Allocate(0)` + `Set(h, 42)` |
| `HeapAllocFreeReuse` | Allocate → Set null → re-allocate (free list path) |
| `Dispatch_10Nops` | VM execution of 10 Nop instructions (dispatch overhead) |
| `Dispatch_100Nops` | VM execution of 100 Nop instructions |
| `Optimize_SimpleArith` | Peephole optimizer on a small foldable program |
| `Optimize_DeepNested` | Peephole optimizer on 100-level deep foldable pattern |

## Completed Optimizations

### 1. Dedicated int Push/Pop with hot/cold path splitting

**Status: Implemented.** `ValueStack` now has dedicated `Push(int)`, `PopInt()`, `PushTwo(int low, int high)`, and `PopTwo()` methods that use direct `_slots[SP++]` instead of `MemoryMarshal`. Each is split into a hot path (single predictable branch, `[MethodImpl(AggressiveInlining)]`) and a cold path (`Grow`, exception throwing, `[MethodImpl(NoInlining)]`).

All 11 int binary arithmetic ops (`Add`, `Sub`, `Mul`, `Div`, `Mod`, `Eq`, `Ne`, `Lt`, `Le`, `Gt`, `Ge`) and 5 int bitwise ops (`BitAnd`, `BitOr`, `BitXor`, `ShiftLeft`, `ShiftRight`) use `PopTwo()` instead of `Pop<(int,int)>()`.

### 2. Breakpoint via opcode patching

**Status: Implemented.** Breakpoints are no longer checked via `state.BreakpointPCs` HashSet every instruction. Instead, `VmDebugger` patches the bytecode at the breakpoint PC with `Int(1)` (5 bytes). On resume, the original bytes are restored and PC is rewound. The VM has zero knowledge of breakpoints. The `instrPc` per-instruction save was removed (moved inside `#if VM_TRACE`).

### 3. Heap.UnsafeGet / UnsafeSet

**Status: Implemented.** `Heap.UnsafeGet(handle)` and `Heap.UnsafeSet(handle, value)` skip bounds checks for callers that already validated via `IsValidHeapHandle`. Updated 15 call sites across Vm.cs and Lowering.cs. Also simplified bounds checks from `handle < 0 || handle >= Count` to `(uint)handle >= (uint)Count`.

### 4. Step counter batching

**Status: Implemented.** The `++steps > MaxSteps` check went from every instruction to every 256 instructions (`(steps & 0xFF) == 0`). The branch is predicted not-taken ~256× more often.

### 5. VmState.ShouldStop

**Status: Implemented.** Replaces `!state.IsSuspended && !state.IsComplete` with a single `!state.ShouldStop` check.

### 6. Reserve() returns void

**Status: Implemented.** The returned `Span<int>` was never used by any caller.

### 7. VM_TRACE compilation flag

**Status: Implemented.** All per-instruction trace output is gated by `#define VM_TRACE` (default: off). When disabled, the `OpcodeNames` array, `TruncateTrace` method, and all `#if VM_TRACE` blocks are completely elided by the JIT.

Enable via:
```bash
dotnet run -c Release /p:DefineConstants=VM_TRACE --project Poly.Benchmarks/Poly.Benchmarks.csproj
```

## Completed Microbenchmarks

All 12 microbenchmarks now in `Poly.Benchmarks/Microbenchmarks.cs`:

| Benchmark | What it measures |
|-----------|-----------------|
| `PushPopInt` | Single `Push(42)` + `PopInt()` round-trip |
| `PushPopInt_Deep` | 100 push + 100 pop sequential |
| `PushPopTwo` | `PushTwo(10,20)` + `PopTwo()` tuple round-trip |
| `HeapAllocGet` | `Allocate(42)` + `Get(h)` round-trip |
| `HeapAllocSet` | `Allocate(0)` + `Set(h, 42)` |
| `HeapAllocFreeReuse` | Allocate → Set null → re-allocate (free list path) |
| `Dispatch_10Nops` | VM execution of 10 Nop instructions (dispatch overhead) |
| `Dispatch_100Nops` | VM execution of 100 Nop instructions |
| `Call_NoArgs` | Function call + return round-trip (zero args) |
| `Closure_SingleCapture` | AllocateClosure(1 capture) + CallClosure + Return |
| `Optimize_SimpleArith` | Peephole optimizer on a small foldable program |
| `Optimize_DeepNested` | Peephole optimizer on 100-level deep foldable pattern |

## Completed Optimizations Summary

| # | Optimization | Status |
|---|-------------|--------|
| 1 | Per-call `VmState` allocation — `VmState.Reset()` pools stack + heap | **Done** (`VmState.Reset` + `ValueStack.Reset` + `Heap.Clear`) |
| 2 | `StrConcat` inline count==2 — avoids `string?[]` array allocation | **Done** |
| 3 | `MaxSteps` configurable — `VmState.MaxSteps` property instead of `const` | **Done** |
| 4 | `TryFold` ReadInt32 — moved inside `op1 == OpCode.PushInt` guard | **Done** |
| 5 | Call + Closure microbenchmarks | **Done** |
| 6 | Relocations dictionary → array | Skipped — already `List<(int,int)>`, linear iteration |
| 7 | `EmitsValue` caching per Block | Skipped — type lookup is O(1), pattern match trivial |

## Future Work

- Measurement campaign: re-run full benchmark suite after Reset() pooling to quantify allocation reduction.
- Top 5 bottlenecks from measurement results.
- SSA form for programs exceeding ~1000 instructions.
- Cross-entity actor policies will drive async lowering requirements (signatures become `Task<Result>`).
