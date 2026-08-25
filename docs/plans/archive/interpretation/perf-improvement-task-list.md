> **ARCHIVED (2026-07-10)** — Do not implement. Superseded by direct AST→VM-ABI (`DirectVmAbiEmitter`). See `docs/plans/archive/interpretation/README.md`.
>
> Original document follows for historical context only.

# Poly VM Performance Improvement — Task List

**Date:** 2026-07-08
**Goal:** Close the gap between Poly VM (NoDebug) and C# native on CPU-bound neurosymbolic workloads.
**Constraint:** Keep implementation complexity proportional to impact.

---

## Task 1: Decoupled 2-Slot Frames (*Full Send*)

**Design:** `docs/vm-decoupled-frames.md` — replace the current single-metadata-slot frame layout with a 2-slot `Frame` struct.

### Current State
- Frame header is 1 metadata `long`: `(returnPC << 32) | (uint)(int)savedFB`
- `FrameBase` + `CachedArgSlots` are mutable fields on `VmState`
- `Return` uses `Array.Copy` to move result from top-of-stack
- `prevBase → 0` bug class: `state.FrameBase < 0 ? 0 : state.FrameBase` at every call site

### Target State
- 2-slot frame: `[ReturnPC, SavedSP]` with FramePos tracking instead of FrameBase
- Eliminate `FrameBase` field entirely (use computed FramePos + `_slots` base)
- `SP = SavedSP; Slot(SP++) = result` — no `Array.Copy`
- Eliminate the fragile `prevBase → 0` pattern

### Subtasks
- [x] **1.1** Rename `FrameBaseLocal` → `FramePosLocal` in `AbiCtx`
- [x] **1.2** Remove `state.FrameBase` from preamble — set `_fp = 0` directly (no more `-1 → 0` gating)
- [x] **1.3** Remove `state.FrameBase` save/restore from `EmitInvoke` — uses `_fp` local directly
- [x] **1.4** Remove `state.FrameBase` sync from `EmitReturn` — uses `_fp` local
- [x] **1.5** Remove `FrameBase` and `OldFrameBase` from `VmState` (`OldFrameBase` → `OldFramePos`)
- [x] **1.6** Remove `StateFrameBaseProperty` static reflection
- [x] **1.7** Remove `FrameBaseInitExpression` property from `AbiCtx`
- [x] **1.8** Update `VmDebugger` — remove `state.FrameBase` dependency (root frame starts at slot 0)
- [x] **1.9** All 94 emitter tests pass, all 10 sieve tests pass
- [ ] **1.10** Re-run benchmarks, verify no regression

---

## Task 2: Static Array Type Specialization (*Done*)

**Goal:** Eliminate the runtime `TypeIs` check in array reads/writes by using analysis-resolved type metadata.

### Current State (before fix)
- `EmitIndexAccess` emitted `if (raw is long[]) ... else ...` on every array read
- `EmitAssignment` emitted the same runtime type check on every array write
- For array-heavy code (nqueens), this was the largest single overhead (~4× factor)

### Implementation
- `EmitIndexAccess`: resolve element type from `ctx.Analysis.GetResolvedType(indexAccess)`
  - Value types → emit cast to `long[]` directly, removing the `TypeIs` branch
  - Reference types → emit cast to `object[]` directly with unbox
  - Unknown → runtime fallback (unchanged)
- `EmitAssignment`: same optimization for array element writes
- Edge case: `FoldCoalesce` bug fixed — `0L` is the ABI falsy/null sentinel, was incorrectly treated as non-null

### Impact
| Benchmark | Before (est.) | After (est.) | Savings |
|-----------|:-----------:|:-----------:|:-------:|
| Sieve | 1.07× | ~1.04× | Marginal (already memory-bound) |
| NQueens | 7.66× | ~4-5× | Significant (tight loop, many array ops) |
| Mandelbrot | 2.06× | ~1.8× | Moderate |
| Collatz | 1.05× | ~1.05× | No arrays, no change |

### Subtasks
- [x] **2.1** Resolve array element type from `ctx.Analysis.GetResolvedType(n)` in `EmitIndexAccess`
- [x] **2.2** Emit direct `long[]` access when element type is known value type
- [x] **2.3** Emit direct `object[]` access when element type is known reference type
- [x] **2.4** Same optimization applied to array write path in `EmitAssignment`
- [x] **2.5** Runtime fallback preserved when type is unknown
- [x] **2.6** All 94 emitter tests pass; zero regression

**Result:** Array-heavy code (nqueens) no longer pays the runtime type check on every access. Estimated ~30-40% improvement on nqueens specifically.

---

## Task 3: Small Lambda Inlining (*Done*)

**Check first:** Is this trivially implementable? If the discovery pass or implementation takes more than ~100 lines, **defer**.

### Current State
- Every lambda invoke pays: frame push (2 words) + closure allocation + ring save/restore + frame pop
- For tiny leaf lambdas (e.g. `(x) => x + 1`), the overhead dominates

### What "Trivial" Looks Like
- In `EmitInvoke`, detect if the lambda body is a single expression (no block, no declarations, no locals)
- If so, inline the body expression directly instead of emitting the frame dance
- No closure needed if no captures are used
- Return the result in the current ring slot

### Subtasks (only if trivial)
- [x] **3.1** Add trivially-inlinable check in `EmitInvoke` — no captures, body is single expression, no locals
- [x] **3.2** In `EmitInvoke`, compile body directly via `EmitInlineInvoke` and skip frame setup
- [x] **3.3** Maps lambda parameter reads to ring slots via `MapInlineParameter` instead of `ParameterRead`

**Fallback:** If >100 lines or >3 files touched, skip entirely.

---

## Task 4: Register File Growable to 32

**Constraint:** Default to current behavior (8 registers) for typical small scopes. Grow up to 32 only when a scope needs it.

### Current State
- `const int RegisterCount = 8`
- `_regUsed = new bool[8]` — hardcoded array
- `_regVars` list is pre-allocated to 8
- When all 8 are used, subsequent variables fall through to raw `_slots[frameBase + slot]` access

### Target State
- `RegisterCount` determined at `CompileFunctionBody` time based on `ScopeAnalysis` or peak scope variable count, clamped to [8, 32]
- Store actual `RegisterCount` in `VmProgram` (add `int RegisterCount` field)
- `_regVars` / `_regUsed` sized dynamically to the count
- When a scope needs > RegisterCount, continue falling through to `_slots` (existing fallback)

### Subtasks
- [x] **4.1** Change `RegisterCount` from `const int` to instance field set in `AbiCtx` constructor
- [x] **4.2** Grows on demand from 8 → 32 via `GrowRegisterFile()` when a scope needs more
- [x] **4.3** Add `int RegisterCount` property to `VmProgram` record
- [x] **4.4** Update `_regVars` / `_regUsed` allocation to use dynamic size
- [x] **4.5** Update `DeclareVariable` to call `GrowRegisterFile()` before falling through to `_slots`
- [x] **4.6** All 94 tests pass; zero regression

**Impact:** Marginal on current benchmarks (max 8 vars in any existing benchmark). Future-proofing for large generated code with many locals.

---

## Task 5: Wire `GetNodeReplacement` Into the Emitter

### Current State
- `ConstantFoldingPass` runs during analysis and registers replacements via `SetNodeReplacement(node, foldedNode)`
- The emitter's `CompileNodeInner` dispatches on node type but **never checks for replacements**
- Result: constant folding analysis runs but the emitter ignores the folded nodes

### Target State
- Before the `node switch` dispatch in `CompileNodeInner`, check `ctx.Analysis?.GetNodeReplacement(node)`
- If a replacement exists, recursively compile the replacement instead
- This is a one-line addition (plus null check)

### Implementation (truly trivial)

```csharp
// At the top of CompileNodeInner, before the switch:
if (ctx.Analysis?.GetNodeReplacement(node) is { } replacement && replacement != node)
    return CompileNodeInner(replacement, ctx);
```

### Subtasks
- [x] **5.1** Add replacement check at the top of `CompileNode` and `CompileNodeWithTracking`
- [x] **5.2** Added `using Poly.Syntax.Analysis` for `GetNodeReplacement` extension method
- [x] **5.3** All 94 emitter tests pass; removed the dead code gap
- [x] **5.4** Fixed `FoldCoalesce` bug: `0L` is now treated as falsy/null in ABI (was treating as non-null)

**Impact:** Makes the existing `ConstantFoldingPass` actually effective. Previously dead analysis work now produces real code that skips runtime evaluation of constant expressions.

---

## Not Doing (Now)

| Item | Reason |
|------|--------|
| **Task 6: Tiered JIT** | Not right now |
| **Double-precision mandelbrot path** | Niche — `BitConverter` round-trip is inherently slow |
