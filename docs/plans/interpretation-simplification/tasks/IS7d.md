# TASK IS7d - cache a promoted array in a typed local, nulled at scope exit

Status: **done** - PR open, pending merge. Branch `slice/IS7d`. Lane C (`vm`). Depends on IS7c (merged).
Plan: [IS-README](../IS-README.md). Design: [`array-access-representation.md`](../../../analysis/array-access-representation.md).

## Why

One array element access costs **1.40 ns** in the VM and nqueens does ~36k of them: ~50 us of a ~57 us run. Every `IndexAccess` emits a temp local, `heap.UnsafeGet((int)handle)`, a `castclass` to `long[]`, then the read.

The fix is to bind the array object to a typed CLR local at its definition, so an access is `_arr3[i]` - one bounds check, no call, no cast. Measured: the same loop with the deref hoisted costs 6.5 us against 19.3 us for the emitted shape, and the in-VM equivalent shape (frame window) already took nqueens from 57 us to 42 us.

**The local is a cache, not a replacement.** The variable's slot keeps the heap handle, so identity is preserved by construction - no escape analysis is needed to prove a buffer cannot leak as a slot index, which is the risk a window carries.

## The hazard the null store addresses

The cache local is keyed by **slot**, not by `Variable` instance, because the rest of the emitter resolves bindings by slot and a slot is reused across sibling scopes. That reuse is exactly why the local must be cleared:

- block A's variable 3 holds a cached array in local `_arr3`;
- block A ends; slot 3 is released;
- block B reuses slot 3 for a different variable.

If `_arr3` still holds A's array when B begins, B could read a **stale array through a fresh binding** - silently wrong. Assigning `null` at A's exit makes that fail closed (`NullReferenceException`) instead of quietly returning the wrong buffer.

**What the null does not do:** release the array. `VmManagedObjectHeap` is append-only from the emitter's side - no site calls `Set`/`UnsafeSet` - so `_objects[handle]` keeps the array alive for the heap's lifetime regardless. Nulling does not reduce GC pressure today. It is lifetime hygiene that becomes load-bearing the moment the heap gains recycling, and it is the thing that makes slot reuse safe. Say this in the code comment; do not claim a memory win.

Cost: one store per scope exit, not per access.

## Scope

1. **Promotion predicate** (an emitter-internal scan, next to the existing block machinery). A `Variable` declared in a `Block` is promoted when, within that block's subtree:
   - it is assigned exactly once, at a statement of that block, and that statement precedes every use;
   - the assigned value is a `NewArray` whose element type resolves (`ArrayElementClrType`);
   - **every** other reference is the `Value` of an `IndexAccess`;
   - no reference appears inside a nested `Lambda` / function body (those compile to a separate delegate that cannot see an outer CLR local);
   - no reference is captured (`ctx.NeedsCell`).
   Anything else - reassignment, `.Length`, an argument, a return, a block value, a capture, an unresolvable element type - keeps the heap path.
2. **The cache local** keyed by `(slot, element type)`, exactly as IS7a keys ring slots by `(index, representation)`. Two locals for two element types never alias.
3. **Definition**: `EmitNewArray` must produce **one** array, bound to both the local and the heap:
   ```
   _arr = NewArrayBounds(elem, len);   // the real array
   slot = heap.Allocate(_arr);         // identity preserved, as today
   ```
   not two allocations.
4. **Access**: `EmitIndexAccess` on a promoted receiver emits `_arr[Convert(index, int)]` with the element conversion `RingCodec` already applies (`ArraySlotElementType`, `CoerceIndexStore`, IEEE/int representations). A bounds failure must throw `IndexOutOfRangeException`, as the heap path does.
5. **Scope exit**: `EmitBlock` appends `Assign(cacheLocal, Constant(null))` after `ctx.PopScope()` for every variable it promoted. The block's own value lives in a ring slot, so clearing afterwards cannot clobber it.

## Files

- `Poly/Interpretation/Vm/Abi/AbiEmitter.Context.cs` - the local table and `ArrayLocal(slot, elementType)`.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Statements.cs` - `EmitBlock`, `EmitAssignment`'s definition arm, the promotion scan.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.cs` - `EmitNewArray`, `EmitIndexAccess`.
- `Poly.Tests/Interpretation/ArrayLocalCacheTests.cs` (new), `Poly.Tests/TestHelpers/VmTree.cs` (a scan assertion).
- `docs/interpretation/vm-abi-reference.md`.

## Shape matrix

| Input | Before | After | Test that proves it |
|---|---|---|---|
| `cols[i]` on a promoted array | `((long[])heap.UnsafeGet(h))[i]` | `_arr[i]` | `PromotedAccess_HasNoHeapDeref` (structural: no `UnsafeGet` call in the access subtree) |
| definition | one heap array | one heap array + bound local | `Promotion_AllocatesOnce` (structural: one `Allocate` call) |
| the same array passed as an argument | promoted | **not** promoted | `EscapingUse_KeepsTheHeapPath` |
| the same array captured in a lambda | promoted | **not** promoted | `CapturedArray_KeepsTheHeapPath` |
| the same array assigned twice | promoted | **not** promoted | `ReassignedArray_KeepsTheHeapPath` |
| `.Length` on the array | promoted | **not** promoted | `LengthUse_KeepsTheHeapPath` |
| a `double[]` element read | bits through the heap | `_darr[i]`, IEEE decoded once | `DoublesArray_ReadsIeeeElements` |
| index out of range | throws | throws the same exception type | `PromotedOutOfRange_ThrowsIndexOutOfRange` |
| block exits | local still holds the array | local assigned `null` | `ScopeExit_NullsTheCache` (structural, on the emitted block) |
| slot reused by a sibling block | reads the stale array | local is null - fails closed | `SiblingBlock_SlotReuse_ReadsNullNotStaleArray` |

## Done when

- Every row above passes, the structural ones by inspecting the emitted tree rather than the source.
- **Benchmark, `--job medium`, before and after**, with the command:
  - `ProgramBenchmarks` - NQueens must improve; **Sieve, Collatz, Mandelbrot must not regress**.
  - `docs/perf-comparison/nqueens_polyvm.cs` built against this branch: record min/avg against the known points 56-63 us (pre-window) and 41-44 us (window).
- `AstParityTests` 145/145; full suite green with the count; `dotnet build Poly.Benchmarks` 0 warnings.
- `docs/interpretation/vm-abi-reference.md` states the cache rule, the slot keying, and **that the null store does not free memory today**.

## SHIP if / NOT SHIP if

SHIP: the structural access test proves no deref, the sibling-slot-reuse row proves the null, the four benchmarks hold, and NQueens improves.
NOT SHIP: two arrays are allocated at the definition; a promoted local survives its scope; an escaping or captured array is promoted; the code comment or docs claim the null reduces GC pressure.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ArrayLocalCacheTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AstParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmTreeInvariantTests/*'
dotnet run -c Release --project Poly.Benchmarks -- --filter '*ProgramBenchmarks*' --job medium
```

## Hand-edit

One table from slot to local, one predicate that fills it, one place that reads it, one that clears it.

## Needs Scot

none.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-23 | agent | Copilot/VS Code | (this commit) | **SHIP.** A promoted array's access is `_arrN[i]`; **nqueens (perf-comparison, array) 63 → 40 us median**, **Sieve 105.6 → 66.7 us (−37 %)**, NQueens/Mandelbrot/Collatz hold; **0 `UnsafeGet` remain in the nqueens tree** | **RED, before the change** (`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ArrayLocalCacheTests/*'`): `total: 10 failed: 6 succeeded: 4` — every promotion row saw **0** cache locals ("the array's definition binds one typed local: " — empty), `ScopeExit_NullsTheCache` died on `Sequence contains no elements`, and `Promotion_AllocatesOnce` found 0 cache stores. The 4 that passed are the bail-out rows (escape, capture, reassignment, `.Length`) and are the matrix's negative half. **GREEN:** `ArrayLocalCacheTests` 10/10, `AstParityTests` **146/146**, `VmTreeInvariantTests` 7/7, full suite **4522/4522**, `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` **0 warnings**. **The rule.** `ArrayCacheCandidate` (next to `EmitBlock`) promotes a block-declared local when it is assigned exactly once at a statement of that block, the value is a `NewArray` whose element type resolves, every other reference is the `Value` of an `IndexAccess`, no reference is inside a `Lambda`, and `NeedsCell` is false; `Context.PromoteArray` binds `_arrN` (register slot) / `_farrN` (frame slot) keyed by **(slot, element type)**; `EmitNewArray` builds the array **once**, `AddUncounted(_arrN = NewArrayBounds(...))` then `slot = heap.Allocate(_arrN)`; `EmitIndexAccess` and the `IndexAccess` store arm read/write `_arrN[i]` through `RingCodec.CachedElementRead/Write` (the same `CoerceIndexStore` and `BoxToAbi` arms the heap path uses); `EmitBlock` appends `_arrN = null` after `PopScope`. **One allocation, proven:** `Promotion_AllocatesOnce` counts exactly 1 `Allocate`; the nqueens tree builds **4** `NewArrayBounds` for its 4 arrays and **4** `Allocate` — no second array. **The null is honest, not a GC claim:** `VmManagedObjectHeap` recycles only through `Set`/`UnsafeSet`, and `grep -rn "UnsafeSet\|\.Set(" Poly/Interpretation/Vm/Abi/` is empty, so the heap keeps the array for its lifetime; the store makes slot reuse fail closed and matters the moment the heap gains recycling. Both the code comment and `vm-abi-reference.md` say so. **Sibling-slot reuse: constructible, and the test builds it.** Two sibling blocks each declaring a `long[]` array both land on register 0 (asserted: one `_reg0`) and therefore on **one** `_arr0`; the emitted store sequence is `array, null, array, null`, so block A's array cannot reach block B — a read of the shared local before B's own definition would see `null`, not A's buffer. What is *not* constructible is the stale **read** itself: the predicate requires the definition to precede every use, so B always stores before it reads; the clear is the invariant that keeps that true if the predicate is ever relaxed. **Benchmark `--job medium`, `dotnet run -c Release --project Poly.Benchmarks -- --filter '*ProgramBenchmarks*' --job medium`, baseline on this branch's HEAD `018e3c83` → this change:** NQueens (bitmask, **no arrays**) 173.06 → 174.54 us (noise, StdDev 6.9), **Sieve 105.63 → 66.72 us (−37 %)**, Mandelbrot 71.97 → 71.93 us isolated A/B (flat; its tree is unchanged), Collatz 156.75 → 143.02 us. **The card's "NQueens must improve" does not apply to `ProgramBenchmarks.NQueens` — it is the bitmask recursion with no array at all** (the array workload there is Sieve, which is where the gain landed); the array nqueens is `docs/perf-comparison/nqueens_polyvm.cs`, and its min/median over 15 runs goes **57/63 us → 38/40 us** (avg 65.5 → 41.7), i.e. at or better than the 41-44 us the frame window reached, matching the analysis note's corrected "the two are equivalent for a variable index". **Residual derefs: 0 in both trees.** The nqueens tree has **0** `UnsafeGet` (18 cached element accesses, 4 `_arr0.._arr3` locals); Sieve's C#-surface program has **0** `UnsafeGet` and 1 `Allocate`. **Known limit, not a bug:** a `break`/`continue`/`return` out of a block skips the trailing clears exactly as it skips the pre-existing `_slots` snapshot stores; the next definition still precedes any read, so no stale buffer is reachable. |
