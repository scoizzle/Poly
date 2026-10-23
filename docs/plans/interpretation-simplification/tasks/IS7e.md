# TASK IS7e - explicit `Recycle(handle)`, and no recycle-on-null

Status: planned. Branch `slice/IS7e`. Lane C (`vm`). Depends on IS7d (merged).
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
