# TASK IS7e - explicit `Recycle(handle)`, and no recycle-on-null

Status: **done** - PR open, pending merge. Branch `slice/IS7e`. Lane C (`vm`). Depends on IS7d (merged).
Plan: [IS-README](../IS-README.md). Context: [`array-access-representation.md`](../../../analysis/array-access-representation.md).

## Why

Two problems with one cause: the heap's lifetime rule is implicit.

1. **`Set(handle, null)` recycles.** `Set`/`UnsafeSet` push the handle to the free list when the value is null, and `Allocate` reuses it. So any code that stores a null into a heap slot can silently hand that handle to a later allocation while another holder still has it. Nothing in the emitter calls either today (`grep` over `Poly/Interpretation/Vm/Abi/` is empty), but the contract invites it, and the failure it produces is a silent alias, not a crash.

2. **Nothing ever frees.** `Interpreter` never calls `Set`, so entries are append-only and a program that allocates in a loop grows the heap once per iteration, forever. Measured cost per allocation is 19.5-22 ns, so 10M iterations is 10M heap entries and a doubling array behind them - unbounded memory for a constant working set.

IS7d made the second one visible: its scope-exit `_arrN = null` cannot release the array, because the heap still holds it. With an explicit recycle at the same point, the null finally means what it looks like - and the comment in `vm-abi-reference.md` that says "does not free memory today" can become true.

## Scope

1. **Add `public void Recycle(int handle)`** to `VmManagedObjectHeap`, with the existing `Recycle` private helper's body. Throws `ArgumentOutOfRangeException` for handle 0 or outside the range, and **`InvalidOperationException` on a double recycle** - fail closed, matching the existing contract for a use-after-free.
2. **`Set`/`UnsafeSet` stop recycling.** A null value stores null; the handle stays live and `Get` returns null. No behaviour change in the VM (no emitter caller), but the contract no longer has a silent free.
3. **Call it where lifetime is proven.** The only place the VM knows a heap object cannot be observed again is a **promoted array at its block exit** - IS7d's predicate already proves the handle is not aliased, not captured, and never used except as an `IndexAccess` receiver. Emit `heap.Recycle(slotValue)` beside the existing `_arrN = null`, reading the handle from the variable's slot before clearing.
4. **Update the tests that assert the old contract.** `InterpretationStabilizationTests:375-384` asserts `Set(h, null)` recycles and that a further `Set`/`UnsafeSet` throws. That behaviour is what this slice removes, so those rows are rewritten against `Recycle` - explicitly, with a comment saying the contract changed and why.

## Files

- `Poly/Interpretation/Vm/VmManagedObjectHeap.cs` - `Recycle`, `Set`, `UnsafeSet`, the class doc.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Statements.cs` - the promoted block exit.
- `Poly.Tests/Interpretation/InterpretationStabilizationTests.cs`, `Poly.Tests/Interpretation/HeapBoundsTests.cs` - the old contract.
- `Poly.Tests/Interpretation/HeapLifetimeTests.cs` (new).
- `docs/interpretation/vm-abi-reference.md` - the array section's memory paragraph.

## Shape matrix

| Input | Before | After | Test that proves it |
|---|---|---|---|
| `Set(h, null)` | handle recycled | stores null; `Get(h)` null; handle not reusable | `SetNull_DoesNotRecycle` |
| `Recycle(h)` then `Allocate(x)` | n/a | the freed handle is reused | `Recycle_ThenAllocate_ReusesTheHandle` |
| `Recycle(h)` twice | n/a | `InvalidOperationException` | `RecycleTwice_FailsClosed` |
| `Recycle(0)` | n/a | `ArgumentOutOfRangeException` | `Recycle_HandleZero_Throws` |
| a promoted array's block exits | array retained | array released; the slot's handle is recycled | `PromotedArray_ScopeExit_RecyclesTheHandle` (structural: a `Recycle` call at block exit) |
| 10k iterations declaring an array in the loop body | heap grows by 10k | heap stays bounded | `ArrayInLoopBody_HeapStaysBounded` |
| an array that escapes (`return` / argument / alias) | not promoted | **not** recycled - the heap keeps it | `EscapingArray_IsNotRecycled` |

`ArrayInLoopBody_HeapStaysBounded` and `EscapingArray_IsNotRecycled` are the pair that matters: one proves the memory win, the other proves it is not achieved by dangling a live handle.

## Done when

- Every row passes; the structural ones inspect the emitted tree.
- `ArrayInLoopBody_HeapStaysBounded` asserts `heap.Count` after 10k iterations is small and constant, not proportional to the count.
- **Full suite green.** The suite is the oracle for the contract change: any test that fails because `Set` no longer recycles is a row to rewrite with a stated reason, not a row to delete.
- Benchmarks (`--job medium`): all four hold. Recycling costs a call at scope exit, so watch the loop-allocating programs - Sieve allocates once and should be flat.
- `docs/interpretation/vm-abi-reference.md` states: handles are stable; only `Recycle` makes one reusable; a promoted array is recycled at scope exit; everything else is retained for the heap's lifetime.

## SHIP if / NOT SHIP if

SHIP: `Recycle` is explicit and double-recycle fails closed; recycle-on-null is gone; a promoted array is recycled and an escaping one is not; the heap stays bounded in a loop.
NOT SHIP: `Recycle` is called on a handle the VM cannot prove dead; `Allocate` can still be reached by a null store; a test asserting the old contract is deleted rather than rewritten; the docs still claim the scope-exit store cannot free memory.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/HeapLifetimeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/HeapBoundsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/InterpretationStabilizationTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ArrayLocalCacheTests/*'
```
Then the full suite and `--job medium`.

## Hand-edit

One public method frees a handle; one call site at a scope exit; the doc comment says which handles are stable and which are reusable.

## Needs Scot

none - the contract change is contained (no emitter caller of `Set`) and the tests that encode the old behaviour are rewritten with reasons.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-23 | agent | Copilot/VS Code | (this commit) | **SHIP.** `heap.Recycle(handle)` is the only way a handle is freed; recycle-on-null is gone; a promoted array is recycled at its block exit — **`heap.Count` after 10k loop-body arrays 10001 → 2**; every escape shape found keeps its handle. | **RED, before the change.** `--treenode-filter '/*/*/HeapLifetimeTests/*'`, first run: `CS1061: 'VmManagedObjectHeap' does not contain a definition for 'Recycle'` ×6 — the four contract rows do not compile against the old API. With `Recycle` added but `Set`/`UnsafeSet` still recycling: `total: 6 failed: 2 succeeded: 4` — `SetNull_DoesNotRecycle` and `UnsafeSetNull_DoesNotRecycle` each `Expected to not be equal to 1 ... but received 1`, i.e. the null store had already handed the handle to the next `Allocate`. Before the emitter change: `total: 9 failed: 2 succeeded: 7` — `PromotedArray_ScopeExit_RecyclesTheHandle` `Expected to be 1 ... but found 0`, `ArrayInLoopBody_HeapStaysBounded` `Expected to be less than or equal to 4 ... but received 10001`. `EscapingArray_IsNotRecycled` passed at that point, which is the safety baseline: nothing recycled before the change, so nothing dangled. **GREEN:** `HeapLifetimeTests` **11/11**, `HeapBoundsTests` 7/7, `InterpretationStabilizationTests` 42/42, `ArrayLocalCacheTests` 10/10, `VmTreeInvariantTests` 7/7, full suite **4534/4534**, `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` **0 warnings**. **`heap.Count`:** 10 iterations → **2** slots and 10 000 iterations → **2** slots (asserted equal to each other and ≤ 4) — the working set, not the iteration count; the same program kept **10001** slots before the slice. **The contract.** `Recycle(int)` throws `ArgumentOutOfRangeException` for handle 0 and for a handle never issued, and `InvalidOperationException` on a **double recycle**. `Set`/`UnsafeSet` store a null as a value — `Get` reads null, the handle stays live, `Allocate` cannot reach the slot — and still reject a handle already on the free-list (the old double-free guard, kept so a freed handle cannot be resurrected behind the free-list's back; neither had an emitter caller, so VM behaviour is unchanged). **The emitter.** `EmitBlock` appends `heap.Recycle((int)slot)` immediately before IS7d's `_arrN = null`, reading the handle from the variable's own storage through `Context.VariableReadRaw` (raw, not capture-aware: promotion already requires `NeedsCell` false, and a cell-backed slot holds a cell handle). `trailing` now includes `releases`, so a block whose last statement is a promoted definition still types its value by the re-read slot. **Escape found while implementing — the important one.** IS7d's predicate scans *references to the variable*, so it cannot see a definition that is itself the block's **value**: a block's value is its last statement's value, and an `Assignment` evaluates to what it assigned, so `{ cols = new long[4]; }` yields the array's handle. The first GREEN emitted a recycle there: a probe read `recycles=1, handle=1, slots=[null, null]`, i.e. a dangling handle handed to the caller — the card's stop condition exactly. Fixed by `IsBlockValue(block, variable)`: the release is withheld when the definition is the last value-producing statement, and `BlockValueArray_IsNotRecycled` now proves the handle stays live (no recycle, `slots[handle] is long[]`; RED before the guard: `recycles=1, handle=1, slots=[null, null]`). `StridedSetBits_KeepsTheHeapPath` proves the node that takes the array object rather than an element is not promoted either. `break`/`continue`/`return` out of the promoted block skip the trailing exit exactly as they skip IS7d's clears and the `_slots` snapshot stores, so the array is retained — a leak, never a freed live handle. **Rewritten tests (none deleted).** `InterpretationStabilizationTests.Heap_SetNull_RecyclesOnce_DoubleFreeThrows` → `Heap_Recycle_IsTheOnlyFree_DoubleRecycleThrows`: the contract changed, because that row asserted `Set(h, null)` frees the handle and a second `Set` throws, which is precisely what this slice removes; it now proves the null store retains and a second `Recycle` is the double free that fails closed. `HeapBoundsTests` carried no null-recycle row — it asserts bounds and error paths — so `Recycle_OutOfRangeHandle_Throws` is added beside `Set_OutOfRangeHandle_Throws`. **Benchmark `--job medium`**, `dotnet run -c Release --project Poly.Benchmarks -- --filter '*ProgramBenchmarks*' --job medium`, median/min, ISB baseline → this change: NQueens 169.4/165.9 → **168.97/164.62 us**, Sieve 158.9/157.1 → **64.95/63.24 us**, Mandelbrot 149.6/136.0 → **70.39/68.83 us**, Collatz 141.4/132.6 → **142.20/134.68 us**; `Allocated` unchanged (671,992 / 3,000 / 480 / 472 B). A/B on this same tree with the release emission disabled: NQueens 170.03, Sieve 60.52, Mandelbrot 66.97, Collatz 139.09 us median — deltas of +4.1 / +7.5 / +4.5 / +1.6 %, inside this job's own run-to-run spread (ISB recorded 2.3x on `--job short`), and **Sieve — the array workload, one allocation (3,000 B) — is flat**, as the card predicted. A second full medium run with the guard in place read 172.66/164.62 mean/min on NQueens, so the spread is the job's, not a recycle cost. **Docs:** `docs/interpretation/vm-abi-reference.md` now states that handles are stable for the heap's lifetime, that only `Recycle` makes one reusable and fails closed on a double free, that a promoted array is recycled at scope exit (with the block-value exception), and that everything the VM cannot prove dead is retained; the "the scope-exit store cannot free memory" paragraph is gone, because it now can. |
