# VM Performance Analysis

## Current Benchmarks

Baseline measurements on Apple M3 Pro, .NET 10 (from `Poly.Benchmarks/InterpreterBenchmarks.cs`):

| Benchmark | Mean | Allocated | vs Baseline |
|-----------|------|-----------|-------------|
| Baseline_Poly | ~0.5 ns | 0 B | 1× |
| LinqJit_Poly | ~2.7 ns | 24 B | ~5× |
| LinqInterp_Poly | ~115 ns | 416 B | ~230× |
| Vm_Poly | ~51 ns | 312 B | ~100× |
| Vm_PolyParam | ~90 ns | ~400 B | ~180× |
| Vm_ClrSimple | ~82 ns | 312 B | ~160× |
| Vm_ClrChain | ~2,400 ns | 608 B | ~4,800× |
| Vm_Nested100 | ~52 ns | 312 B | ~100× |

The VM is ~100× slower than raw C# for pure arithmetic, mainly due to:
1. Per-execution `VmState` allocation (312 B)
2. Stack push/pop through generic `MemoryMarshal` paths
3. Instruction dispatch through a 68-case switch

## Top 5 Bottlenecks

### 1. Per-call VmState allocation (312 B/op)

Every `Vm.Execute()` call allocates a new `VmState`, which creates a new `Heap` (internal `List<object?>` + `Stack<int>`) and rents a `ValueStack` from `ArrayPool`.

**Fix:** Add `VmState.Reset()` that clears the stack (`SP = 0`) and heap (clears `_objects`, empties `_freeSlots`) without deallocating internal arrays. The benchmark would reuse a single `VmState`, eliminating nearly all 312 B/op.

**Effort:** Medium. `Reset()` must restore all state to post-construction values: `PC = 0`, `FrameBase = -1`, `Status = Running`, `BreakpointPCs.Clear()`, `Dispose()` of previous heap objects.

### 2. Generic Push\<T\>/Pop\<T\> MemoryMarshal overhead

Every instruction dispatches through `Push<int>` or `Pop<int>`. The generic path calls `SlotCountOf<T>()`, computes `(Unsafe.SizeOf<T>() + 3) / 4`, then wraps spans with `MemoryMarshal.AsBytes` and `MemoryMarshal.Write/Read`. For `int` (1 slot), this is ~5-10× more expensive than `_slots[SP++] = value`.

**Fix:** Add non-generic fast paths:

```csharp
public void Push(int value) {
    if (SP >= _slots.Length) Grow();
    _slots[SP++] = value;
}
public int PopInt() {
    if (SP < 1) throw ...;
    return _slots[--SP];
}
```

Same for `Pop<(int,int)>` — replace with two `PopInt()` calls. This cuts opcode dispatch overhead by ~30-50%.

**Effort:** Low.

### 3. BreakpointPCs check every instruction

Every instruction evaluates `state.BreakpointPCs is not null && state.BreakpointPCs.Contains(instrPc)` (line 63). The `instrPc` save (line 47) exists purely for this check. With no breakpoints set, the null check is a predicted-not-taken branch.

**Fix:** Hoist the breakpoint check out of the hot loop:

```csharp
if (state.BreakpointPCs is { } bps) {
    RunWithBreakpoints(state); // saves instrPc, checks bps
} else {
    RunFast(state); // no instrPc save, no breakpoint check
}
```

Both loops share the same opcode dispatch logic but the fast path eliminates one register save and one branch per instruction.

**Effort:** Low. Requires duplicating the main loop — one copy with breakpoint support, one without.

### 4. StrConcat array allocation per concatenation

`StrConcat` allocates a `string?[count]` array on every operation. For a 2-string concat (the common case), the array allocation costs as much as the concatenation itself.

**Fix:** Inline small counts:

```csharp
case OpCode.StrConcat:
    int count = state.Stack.PopInt();
    if (count == 2) {
        var b = ResolveHeapValue(state, state.Stack.PopInt())?.ToString();
        var a = ResolveHeapValue(state, state.Stack.PopInt())?.ToString();
        state.Stack.Push(state.Heap.Allocate(string.Concat(a, b)));
    } else {
        var parts = new string?[count];
        for (int i = count - 1; i >= 0; i--)
            parts[i] = ResolveHeapValue(state, state.Stack.PopInt())?.ToString();
        state.Stack.Push(state.Heap.Allocate(string.Concat(parts)));
    }
    break;
```

**Effort:** Low.

### 5. Redundant heap range checks

Many opcodes call `IsValidHeapHandle(state, handle)` before calling `state.Heap.Get(handle)`, but `Heap.Get` does its own range check. This is double validation.

**Fix:** Add `Heap.UnsafeGet(int handle)` that skips the range check, and use it in callers that already validated via `IsValidHeapHandle`. Or alternatively, remove the range check from `Heap` entirely and make all callers responsible.

**Effort:** Low.

## Secondary Opportunities

| Issue | Effort | Impact |
|-------|--------|--------|
| Hardcoded 100K step limit | Low | Low — expose via `VmState.MaxSteps` |
| Dead span allocation in `Reserve` | Low | Low — callers never use the returned span |
| `EmitsValue` called redundantly per Block node | Low | Low — cache result per node |
| `TryFold` calls `ReadInt32` unconditionally | Low | Low — guard with opcode check first |
| Relocations dictionary lookup overhead | Low | Low — use array-based PC map |

## Microbenchmark Suite (Wishlist)

The current `InterpreterBenchmarks` tests whole-program end-to-end throughput. Targeted microbenchmarks for individual VM components would make optimization impact easier to measure:

| Component | Microbenchmark |
|-----------|---------------|
| Stack | `Push<int>` / `Pop<int>` throughput (non-generic vs generic) |
| Stack | `Push<(int,int)>` / `Pop<(int,int)>` throughput (tuple vs two PopInt) |
| Heap | `Allocate` / `Get` / `Set` throughput |
| Dispatch | No-op loop (measure dispatch overhead) |
| Call | `Call` + `Return` round-trip |
| Closure | `AllocateClosure` + `LoadUpvalue` + `CallClosure` throughput |
| Optimizer | `Optimize(Bytecode)` throughput on typical programs |

## `VM_TRACE` compilation flag

The per-instruction execution trace is gated by `#define VM_TRACE` (default: off). When enabled, every instruction dispatches through:

1. `Enum.GetNames<OpCode>()` array lookup for opcode name
2. `SourceMap.TryGetValue(instrPc)` for node ID
3. `NodeDescriptions.TryGetValue(nodeId)` for node description
4. `FormatStack()` for stack state
5. `TruncateTrace()` for description truncation
6. `TextWriter.WriteLine()` for output

When disabled (default), the JIT completely elides all of these — no array allocation, no dictionary lookups, no string formatting, no writer calls. The `OpcodeNames` array, `TruncateTrace` method, and all `#if VM_TRACE` blocks are compiled away.

Enable via:
```bash
dotnet run -c Release /p:DefineConstants=VM_TRACE --project Poly.Benchmarks/Poly.Benchmarks.csproj
```
