# Array access: cache the array in a typed local, don't window its elements

**Status:** research + proposal. No production change accompanies this note. Measurements taken 2026-10-23 on `plan/interpretation-simplification`.
**Scope:** `Poly/Interpretation/Vm/Abi/` — `IndexAccess` emission, `SlotLocation`, the variable/storage model.
**Input:** [#262](https://github.com/scoizzle/Poly/pull/262) (frame windows) and the measurement in [`docs/perf-comparison/README.md`](../perf-comparison/README.md) §Why nqueens is the weakest row.

## The problem, measured

One array element access costs **1.40 ns** in the VM, and nqueens does ~36k of them: ~50 us of a ~57 us run. The emitted shape is why — every `IndexAccess` emits a temporary local, a call to `heap.UnsafeGet((int)handle)`, a `castclass` to `long[]`, then the element read.

Three designs, and the hand-written cost of each shape (nqueens, 128 runs, min):

| Design | Per access | Measured |
|---|---|---|
| heap deref per access (today) | call + `castclass` + bounds check | 19.3 us |
| frame window ([#262](https://github.com/scoizzle/Poly/pull/262)) | index temp + explicit bounds check + 2 adds + `_slots` bounds check | **41-44 us in the VM** (from 56-63) |
| **cached typed local (proposed)** | one bounds check, nothing else | **6.5 us** |

The window removed the deref and still measured 41 us, which says the ceremony it added back costs more than the deref it removed. Hoisting the real array to a CLR local costs *nothing* per access.

## The proposal

At the definition of a local whose initializer is `new T[n]`, keep the heap allocation **and** bind the array object to a typed CLR local in the emitted lambda body:

```csharp
// definition
var handle = heapAllocate(new long[8]);   // variable's slot keeps this, as today
long[] _arr3 = new long[8];               // and the array is reachable directly

// every element access on that variable
_arr3[i]                                  // one bounds check, no deref, no cast
```

Every other use of the variable — assignment, argument, return, capture, `.Length`, aliasing — keeps the existing handle path through its slot. The local is a **cache, not a replacement**.

That difference is what makes it cheaper than the window to get right:

- **identity is preserved by construction.** The variable's slot still holds the handle, so nothing can observe a difference. A window replaces the representation and therefore needs an escape analysis proving the buffer cannot leak; a cache needs only that the name is not rebound.
- **the heap contract already guarantees stability.** `VmManagedObjectHeap` exposes `Set`/`UnsafeSet`, but **no emitter site calls either** (`grep -rn "UnsafeSet\|\.Set(" Poly/Interpretation/Vm/Abi/` is empty), so entries are append-only and a handle maps to one object for the heap's lifetime. A cached element cannot go stale. If a future change starts recycling, this note's assumption is the thing to re-check.
- **assignment order is the only predicate.** The local must be assigned before every use on every path, so the definition must dominate the uses.

## Conditions (all must hold, else the handle path)

1. The initializer is `new T[n]` with `T` an inline slot type and `n` a constant — though, unlike the window, **`n` need not be small**, because nothing is laid out in frame slots.
2. The variable is assigned exactly once, at a definition that dominates every use.
3. No use inside a nested lambda or function body: those compile to a separate delegate that cannot see an outer CLR local. Keep the handle path there.
4. A reassignment, a capture, or a name collision with another binding falls back.

Conditions 2-4 are #262's predicate; condition 1 is strictly looser, which is part of the argument for the cache.

## What it does not fix

- **The `new T[n]` allocation still happens.** A window avoids the allocation; a cache does not. For a buffer allocated inside a hot loop, that matters and a window (or escape-to-locals) is the right answer. Nqueens allocates once per run.
- **The heap-resize path** still runs on allocation (`Allocate` may grow `_objects`), but once, at the definition.

## How to prove it

The existing ladder is the oracle: build `docs/perf-comparison/nqueens_polyvm.cs` against the change and compare with 56-63 us (pre-#262) and 41-44 us (#262). The hand-written ceiling for the cached shape is 6.5 us, so a in-VM result above ~20 us means the remaining cost is the per-statement machinery around the access rather than the access itself — measure that separately rather than assuming the design failed.

Correctness gate: `AstParityTests` (145 rows) plus a `VmTree` structural assertion that a promoted variable's element access contains no `UnsafeGet` call and no `castclass`.

## Recommendation

**Implement the cache first.** It is the smaller predicate on a sounder invariant (identity preserved), it subsumes the window's benefit for the nqueens case, and it leaves the window available for the case the cache cannot help — a buffer allocated inside a hot loop, where avoiding allocation is the point.

If both land, the promotion is one predicate with two outcomes: a constant small length in a hot allocation site promotes to slots; everything else that passes dominance caches the array object.

## Note on naming

"Special slot/ring promotion" describes both designs, so the distinction worth keeping in the code is **cache** (the handle is still the truth) versus **window** (the handle is replaced by slot indices). One preserves identity by construction; the other has to prove it.
