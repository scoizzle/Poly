> **ARCHIVED (2026-07-10)** — Do not implement. Superseded by direct AST→VM-ABI (`DirectVmAbiEmitter`). See `docs/plans/archive/interpretation/README.md`.
>
> Original document follows for historical context only.

# Anti-Pattern 006: Completeness-Driven µop Inventory

**Problem:** 14 of 37 µop types are never emitted by lowering. The 9 immediate-bearing variants (`NegImmOp` through `ShrImmOp`) exist because the `EmitBinary` switch lists them as possible fusion targets, but lowering never produces the source pattern that triggers them. This is "design the complete instruction set first, then write the code generator" — the opposite of "build working code before extracting abstractions."

## Plan

1. **Wire up the 9 imm variants in lowering's `EmitBinary`.** The `EmitBinary` method creates a temporary µop via `makeOp()`, pattern-matches its type, then selects the Imm variant. The 9 unwired variants (`NegImmOp` through `ShrImmOp`) have entries in the switch but no matching source. Either:
   - Add the source patterns that trigger them (e.g., constant-folding the unary `Neg`/`Not` operands in `EmitNode`), or
   - Remove the µop types and their switch arms if the patterns are never expected to occur.

2. **Remove `CmpLocalJmpOp` and `CmpLocalLeOp`** if lowering is not expected to emit fused compare-and-jump µops. These were designed for optimized while-loop lowering but the fusion never materialized.

3. **Document the remaining 3 test-only µops** (`IncLocalOp`, `BatchReduceOp`, `CountBitsOp`) as test/benchmark infrastructure. Keep them but mark them explicitly so future readers know they're not emitted by lowering.

**Lines saved:** ~90-130 depending on how many are removed.

**Risk:** Low for removal (they're never emitted, so no code path breaks). Medium for wiring up (adding source patterns changes lowering behavior and adds test requirements).

## Recommendation

Wire up the 9 imm variants if there's a concrete source pattern that should produce them (e.g., `-constant` should produce `PushOp(-constant)` instead of `PushOp(constant); NegOp`, which is already handled). Remove `CmpLocalJmpOp` and `CmpLocalLeOp` — the while-loop fusion they were designed for was never built.
