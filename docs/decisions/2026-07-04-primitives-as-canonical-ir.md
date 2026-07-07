# ADR: Primitive Instruction Set as Canonical IR (Superseded)

**Date:** 2026-07-04
**Status:** Superseded — the primitive instruction set has been removed from the critical
execution path. The AST is the canonical symbolic form; the `DirectVmAbiEmitter` performs
direct AST-to-VM-ABI lowering. See `docs/plans/pruning-primitives-plan.md`.

## Context

The 2026-05-31 neurosymbolic platform vision and the 2026-06-24 compiler framework plan
described a **separate canonical IR** (`Poly/Ir/`) as a block-structured CFG with SSA values,
distinct from the `PrimitiveNode` instruction set used by the VM.

During development, the `PrimitiveNode` instruction set became the canonical IR for the VM
execution engine, with `ToPrimitives()` expansions from each AST node type. This was the
approach used throughout the initial VM implementation.

## Decision (Superseded)

As of 2026-07-07, the `PrimitiveNode` instruction set and its entire expansion infrastructure
(`ToPrimitives()`, `ExpansionPass`, `ProgramCompiler`, `RingAllocator`, `PrimitiveLinker`,
etc.) have been removed from the codebase. The VM execution path now uses **direct AST lowering**
via `DirectVmAbiEmitter`, which walks the analyzed AST and emits `System.Linq.Expressions`
trees targeting `VmState` directly — no intermediate primitive flattening or reconstruction.

## Rationale for Superseding

1. **The AST is the canonical symbolic form.** There was no benefit to flattening structured
   AST nodes to a flat `PrimitiveNode[]` array and then reconstructing control flow, closures,
   and exception handling from that array. The direct path is simpler, faster to compile,
   and preserves AST structure for debugging.

2. **Information loss on flattening.** Lowering structured AST → flat primitives → reconstructed
   control flow lost information that the direct path preserves (AST node identity for debug
   position, structured EH as native `TryCatchFinally`, etc.).

3. **Accidental complexity.** The primitive pipeline required `RingAllocator`, `PrimitiveLinker`,
   `ExceptionTableBuilder`, `PcToRingDepth`, and side tables that the direct path renders
   unnecessary. The direct path uses ~1600 lines of emitter code versus thousands across the
   primitive infrastructure.

4. **No backend required primitives.** The only consumer of primitives was the VM execution
   engine, which now uses direct lowering. C# code generation and LINQ expression generation
   consume the analyzed AST directly.

## Consequences

- The `Poly/Ir/` module is not created (same as original decision).
- All `ToPrimitives()` overrides on AST nodes have been removed.
- The `Poly/Syntax/Primitives/` directory has been deleted.
- The VM execution path is cleaner, faster to compile, and structurally sound.
- A future decision could reintroduce a thin export/portable IR if needed, but only with
  real consumers driving the requirement.
- `PrimitiveNode` gains `InputSlots` and `ResultSlot` with default (empty/null) implementations.
- A `Phi` primitive type is added to the primitive set.
- `Module` and `BasicBlock` types are added to `Poly/Syntax/Primitives/` as optional wrappers.
- `ProgramCompiler` gains `CompileModule()` for dataflow-aware emission, with `CompilePrimitives()`
  as a backward-compatible shim.
- **Lowering discipline (2026-07-06)**: `ToPrimitives()` must not discard information. It is the point to *expand* known analysis metadata (exception regions, value representations, call sites, dataflow facts, etc.) alongside the primitives. The output (primitives + metadata) should minimize the need for later reconstruction. The AST remains the primary symbolic form; enriched primitives serve execution.
- Expansion methods (`ToPrimitives()`) can be migrated incrementally to produce explicit slots,
  starting with expression nodes (`Add`, `Subtract`, etc.) and control flow (`IfStatement`).
- The `docs/experiments/interpretation-compiler-framework-plan.md` is updated to reflect
  that the IR migration target is an enhanced primitive format, not a separate type system.
