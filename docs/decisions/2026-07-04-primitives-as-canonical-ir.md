# ADR: Primitive Instruction Set as Canonical IR

**Date:** 2026-07-04
**Status:** Accepted

## Context

The 2026-05-31 neurosymbolic platform vision and the 2026-06-24 compiler framework plan
described a **separate canonical IR** (`Poly/Ir/`) as a block-structured CFG with SSA values,
distinct from the `PrimitiveNode` instruction set used by the VM.

During development of 25+ primitive types and the peephole optimizer (`PrimitiveOptimizer`),
two realities emerged:

1. **The primitives already are the IR.** Every semantic concept planned for the canonical IR
   exists in the primitive set: operations (`BinaryOp`, `UnaryOp`), memory (`LoadLocal`,
   `StoreLocal`, `ArrayLoad`, `ArrayStore`), control flow (`Goto`, `CondGoto`, `Label`),
   functions (`Call`, `CallExternal`, `Parameter`), and optimizations (`IncLocal`, `DecLocal`).

2. **A separate IR duplicates the instruction set.** Every AST node would need two lowering
   targets (`ToPrimitives()` and `Emit(IrContext)`). Every backend would need two paths.
   A conversion pass from primitives → IR would be required, adding complexity with zero
   semantic gain.

## Decision

**The `PrimitiveNode` instruction set IS the canonical IR.** Rather than creating a second
representation, we enhance the primitive format with explicit dataflow information:

- `ValueSlot` — lightweight value identity (index into the module's value table)
- `InputSlots` / `ResultSlot` — optional explicit dataflow edges on each primitive
- `Phi` — explicit merge point primitive
- `BasicBlock` — contiguous non-branching primitive sequence bounded by terminators
- `Module` — container for `BasicBlock` lists, value slots, and metadata

These additions make the primitive format an explicit SSA IR while maintaining full
backward compatibility: primitives without explicit slots fall back to the existing
`StackEffect`-based dataflow simulation.

## Rationale

- Eliminates the need for a separate `Poly/Ir/` module, `EmissionContext`, `GenerationPass`,
  and all the infrastructure described in the 2026-06-24 plan.
- The primitive set has reached semantic completeness (25+ types covering all planned IR
  instructions). Adding `Phi` completes the SSA picture.
- Existing `ComputePrimitiveConsumedPcs` already computes use-def chains from `StackEffect`.
  Explicit slots make this a direct read instead of a simulation, but the information is
  the same.
- Every backend (VM, C# codegen, future WASM, AOT) already consumes primitives. Enhancing
  the primitive format improves all backends simultaneously.

## Consequences

- `Poly/Ir/` is not created. The compiler framework plan's sections describing a separate
  IR are superseded by this decision.
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
