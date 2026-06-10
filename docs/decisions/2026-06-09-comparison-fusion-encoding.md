# ADR: Comparison Fusion — Lowering-Level, Not Opcode-Level

**Date:** 2026-06-09
**Status:** Accepted

## Context

The comparison+branch sequence `Eq; JumpIfFalse L` pushes a 0/1 boolean, then immediately pops it and branches. This is two chunk decodes for one logical operation.

One proposed optimization: change all comparison opcodes to push the raw `a - b` difference instead of 0/1. Branch opcodes would decode the condition directly from the difference signal:

| Branch     | Condition   | Interpretation |
|------------|-------------|----------------|
| JumpIfZero | branch if diff == 0 | a == b |
| JumpIfNotZero | branch if diff != 0 | a != b |
| JumpIfNegative | branch if diff < 0 | a < b |
| JumpIfNonNeg | branch if diff >= 0 | a >= b |

This would make all six comparison opcodes identical (a subtraction + sp--), removing one operation from every comparison decode.

## Decision

**Rejected.** The subtraction trick produces incorrect results for signed overflow.

If `a` and `b` span the full 64-bit range, `a - b` overflows: `long.MaxValue - (-1)` wraps to `long.MinValue`, so `diff < 0` is true even though `MaxValue > -1` is true. The CPU's `cmp; cset` handles this correctly because the condition flags (N, Z, C, V) capture the mathematical comparison, not the arithmetic result.

A correct implementation must use `cmp; cset` (or equivalent) per comparison, which is exactly what the current 0/1 opcodes do.

## Consequences

- Comparison opcodes continue to push canonical 0/1.
- The optimization moves to the **lowering pass**: when the encoder finds `Comparison; JumpIfFalse` feeding the same stack slot, it emits a single fused chunk that performs the comparison and branch directly, skipping the intermediate boolean entirely.
- The fused chunk is a new super-instruction opcode (e.g. `CmpEqJumpIfFalse`), not a change to existing comparison semantics. It uses the same `cmp; cset` logic internally — the saving is in eliminating one chunk decode and one stack push/pop, not in the arithmetic.
- This avoids the overflow trap because the fused instruction never computes `a - b` as a value — it only feeds the condition flags to the branch.
- The subtraction trick is useful elsewhere as a **lowering technique** for unsigned comparisons (`UDiv` check, bounds checks via `(uint)val < (uint)limit`) where overflow is impossible because the operands are known-non-negative. That's a separate optimization.
