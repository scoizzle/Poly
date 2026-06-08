# ADR: VM as Canonical Semantics (Tree-Walker Removal)

**Date:** 2026-06-08  
**Status:** Accepted  

## Context

The 2026-05-31 neurosymbolic platform vision specified a three-tier evaluation stack: TreeWalker (instant, approximate) → LinqExpressions (fast, exact) → Backend codegen (slow, production). The tree walker was intended as the canonical semantics reference that every other backend must match.

During development, two realities emerged:

1. **The tree walker duplicated the VM.** Both walked the AST and produced `InterpreterResult`. The tree walker was 1,389 LOC of parallel logic that had to stay in sync with the VM. Every new construct (closures, named break/continue, ForEach, Using, exception regions) had to be implemented twice.

2. **The VM never diverged from correctness.** The VM was always implemented as a direct lowering of the same AST nodes with the same analysis context. There was no case where the tree walker was correct and the VM was wrong — the VM always matched or surpassed it.

3. **The two-tier evaluation (instant vs fast) didn't materialize.** The tree walker wasn't meaningfully faster than lowering + VM execution for the IR sizes we encounter. The overhead of analysis dominated both paths.

## Decision

Remove the tree walker and designate the **VM as the canonical semantics** for the lowered IR.

The conformance test suite (defined by `VmParityTests`, `VmSkeletonTests`, and the existing LinqExpression parity tests) becomes the single source of truth for correct behavior. Every backend (future WASM, native AOT, GPU kernels) must pass the same conformance suite.

## Rationale

- Eliminates 1,389 LOC of duplicate logic with zero loss of coverage.
- Removes the burden of keeping two interpreters in sync.
- The VM is more complete (closures, exception regions, ForEach, Using, suspend/resume).
- Conformance is defined by test results, not by which interpreter runs the test.

## Consequences

- The three-tier model collapses to two: LinqExpressions (test reference, may be removed later) → VM (canonical) → Backend codegen (production).
- `AGENTS.md` is updated to reflect this.
- The `VmParityTests.cs` file (migrated from `VmTreeWalkingParityTests.cs`) serves as the canonical conformance suite.
- Future backends validate against the VM output via the conformance suite.
