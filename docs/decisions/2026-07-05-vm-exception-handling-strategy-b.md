# VM Exception Handling — Strategy B (Runtime Dispatch / Side Table)

**Date:** 2026-07-05  
**Status:** Accepted  
**Supersedes:** The Strategy A recommendation in §4.12 of `docs/interpretation-system-architecture-review.md` (Rev 1.5). Strategy B is now the primary approach following external comparison against LLVM, CLR, and JVM practice (§4.22).  
**Context documents:**
- [`docs/interpretation-system-architecture-review.md`](../interpretation-system-architecture-review.md) §4.12 (EH design chapter), §4.22 (external comparison) — living review; some sections predate direct AST ABI
- [`docs/plans/archive/interpretation/interpretation-system-resolution-plan.md`](../plans/archive/interpretation/interpretation-system-resolution-plan.md) — **archived** INT-018 tracker (historical only)
- `2026-06-08-vm-as-canonical-semantics.md` (VM authority)
- `2026-06-08-breakpoint-architecture.md` (sibling runtime-control ADR)
- Current EH reality: structured `Expression.TryCatchFinally` from `DirectVmAbiEmitter` (prefer code over µop-era strategy text when they conflict)

## Problem

The VM currently has no working exception handling: `PrimThrow` and `PrimThrowProtected` were no-ops until Phase 1a (2026-07-05), and `RegionMarker` remains a no-op. The flat `PrimitiveNode[]` µop stream with in-band `RegionMarker` annotations is structurally incompatible with structured EH — catch/finally bodies execute unconditionally after try bodies when `ExceptionRegionMetadata` is present (C-017, C-018).

Two fundamentally different implementation strategies exist. This ADR adopts **Strategy B (Runtime Dispatch / Side Table)**.

## Strategy B Design

### Architecture

Instead of restructuring the flat µop array into nested `Expression.TryCatchFinally` blocks (Strategy A), Strategy B adds a **side table** to `VmProgram` that maps PC ranges to handler addresses, and wraps the compiled delegate in a single `Expression.TryCatch` with a dispatch expression.

```
VmProgram
├── Delegate: Action<VmState>    ← Main body (flat µop compilation, as today)
├── Functions: Action<VmState>[] ← Handlers compiled independently (+ closures)
├── ExceptionRegionTable         ← Side table (new)
│   └── entries[TryStartPc, TryEndPc, HandlerFuncIndex, Kind, CatchType?, ParentIndex]
└── RootValueKind
```

### Components

**1. ExceptionRegionTable** — A flat list of entries, each describing one protected region:

```
record ExceptionRegionEntry(
    int TryStartPc,          // inclusive PC of try body start
    int TryEndPc,            // exclusive PC of try body end
    int HandlerFuncIndex,    // index into VmProgram.Functions for the handler delegate
    RegionKind Kind,         // Catch | Finally | UsingDispose
    string? CatchTypeName,   // assembly-qualified type name for catch filters
    int ParentRegionIndex    // -1 for top-level; index of enclosing region
)
```

**2. Handler compilation** — Each catch/finally/dispose body is compiled as an independent `Action<VmState>` delegate, reusing the same infrastructure as closure function bodies. Ring allocation runs fresh per handler (depth 0 at entry — stack is unwound at throw).

**3. Main body** — The flat µop array is compiled as today, with handler body PCs either skipped or included as unreachable no-ops (design choice: skip to reduce delegate size). The main delegate is wrapped in:

```csharp
Expression.TryCatch(mainBody,
    Expression.Catch(typeof(Exception), dispatchVar =>
        EmitExceptionDispatch(dispatchVar, state, regionTable)))
```

**4. Dispatch expression** — At runtime, the catch handler:
1. Reads the faulting PC (captured from `VmState.ProgramCounter` at the throw site)
2. Scans `ExceptionRegionTable` for the innermost matching region
3. If catch: checks `CatchTypeName` against the thrown exception's type; on match, stores exception in the handler's frame slot and invokes `Functions[handlerIndex](state)`
4. If finally: invokes the finally handler unconditionally, then continues unwinding to the parent region
5. If no matching region: rethrows the exception via `Expression.Rethrow`

### Key Properties

| Property | Strategy A (Nesting) | Strategy B (Side Table) |
|----------|---------------------|-------------------------|
| **IR structure** | Tree-on-flat round trip | Flat always — side table external |
| **Ring allocation** | Per-region (independent pass) | Single pass on full array; fresh per handler |
| **Handler compilation** | Inline in main expression | Independent `Functions[]` entries |
| **Serialization** | CLR-specific (Expression tree) | Portable (side table is plain data) |
| **Precedent** | None (novel) | LLVM, CLR, JVM (established practice) |
| **Implementation complexity** | Lower initial effort; higher maintenance | Higher initial effort; lower maintenance |
| **Nested EH** | Marker stack | `ParentRegionIndex` traversal |

### Rationale for Choosing Strategy B

1. **Aligned with established compiler practice.** LLVM (landingpad + personality function), CLR (exception clause table), and JVM (exception table) all use side tables. Strategy B is how every mature compiler handles EH.

2. **Preserves flat emission.** The existing `TryCatchFinally.ToPrimitives` flat µop stream needs no restructuring. A single `ComputePrimitiveRingDepths` pass covers the full array.

3. **Reuses existing infrastructure.** Handler compilation uses the same `ProgramCompiler.CompilePrimitives` path as closure function bodies — `VmProgram.Functions` already supports independent `Action<VmState>` delegates.

4. **Strictly more serialization-friendly.** `ExceptionRegionTable` is a plain data structure with ints and strings — easily serializable to/from bytecode (INT-019). Strategy A's LINQ Expression nesting has no portable serialization.

5. **Strategy A may still be useful** for simple try-finally patterns (e.g., `using` disposal) as a lightweight optimization. The two strategies are not mutually exclusive.

### Implementation Plan (5 Phases)

| Phase | Tasks | Deliverable |
|-------|-------|-------------|
| **Phase 1** (done 2026-07-05) | Wire `EmitThrowOp` into `PrimThrow` switch; add throw tests | Unprotected throws propagate CLR exceptions |
| **Phase 2** | Add `ExceptionRegionTable` type; populate from analysis; compile handlers as `Functions` entries; wrap main body in `Expression.TryCatch` with dispatch | Try-finally and `using` disposal |
| **Phase 3** | Catch clause dispatch with type filtering + catch variable binding | Try-catch |
| **Phase 4** | Nested EH via `ParentRegionIndex` depth-first scan | Nested try/catch/finally |
| **Phase 5** | Cross-engine parity tests; update vm-gap-analysis feature matrix | EH ✓ in matrix |

### Changes to Existing Behavior

- `RegionMarker` primitives become **compile-time metadata only** — they are used by `BuildExceptionRegionTable` to determine PC ranges, but generate no runtime code.
- `PrimThrowProtected` will be wired in Phase 3 to set the protected-throw path (capture PC + exception state, then enter dispatch).
- `ThrowStatement.ToPrimitives` remains unchanged — it already emits `ThrowProtected` for throws inside protected regions.

## Related

- INT-018 tracker (historical): [`docs/plans/archive/interpretation/interpretation-system-issues.md`](../plans/archive/interpretation/interpretation-system-issues.md)
- Resolution plan Phase 1 (historical): [`docs/plans/archive/interpretation/interpretation-system-resolution-plan.md`](../plans/archive/interpretation/interpretation-system-resolution-plan.md)
- Analysis pass: `ExceptionRegionAnalysisPass` (produces `ExceptionRegionMetadata`)
