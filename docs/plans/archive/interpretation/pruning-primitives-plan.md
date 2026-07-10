> **ARCHIVED (2026-07-10)** — Do not implement. Superseded by direct AST→VM-ABI (`DirectVmAbiEmitter`). See `docs/plans/archive/interpretation/README.md`.
>
> Original document follows for historical context only.

# Pruning the Primitive Expansion Path and ToPrimitives Implementations

**Date:** 2026-07-07  
**Status:** Planned  
**Owner:** (to be assigned)  
**Related:**
- `docs/experiments/direct-ast-lowering-spike.md`
- `docs/decisions/2026-07-04-primitives-as-canonical-ir.md` (to be updated)
- `docs/plans/archive/interpretation/interpretation-system-resolution-plan.md`
- `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs`
- `Poly/Syntax/Node.cs`

## Background & Rationale

The project has completed the experimental direct AST-to-VM-ABI lowering path (`DirectVmAbiEmitter`). This path:

- Walks analyzed AST nodes directly
- Uses a local ring discipline
- Emits structured LINQ Expressions against the bespoke `VmState` ABI
- Supports closures/captures via heap snapshots
- Uses native `TryCatchFinally`, `Loop`, etc.
- Surfaces AST `Node` + `Node.Id` directly for debug/suspend position

Validation (non-trivial suspend/resume with captures, EH, format comparison via `DumpTree`, full test suite) showed that the old flattening + reconstruction tax (`ToPrimitives` → `ExpansionPass` → `PrimitiveExpansionMetadata` → `ProgramCompiler` + side tables) is largely accidental complexity for the VM execution path.

**Core principles applied:**
- Keep only what measurably helps the customer.
- The domain model (AST) is the key artifact.
- Build working code before extracting abstractions (the direct emitter is now that working code).
- Engineer end-to-end behavior with clear ownership (Interpretation owns VM ABI lowering).

As of 2026-07-07 the critical path has been switched (`Interpreter.Compile` now uses direct lowering by default). Many old reconstruction files have already been removed from `Interpretation/Vm/`. The remaining work is to finish the cleanup.

## Goals

1. Make the direct AST lowering path the **only** supported lowering for VM execution.
2. Completely remove the primitive expansion machinery from the critical path and from source where it is no longer used.
3. Remove all `ToPrimitives` implementations from concrete AST nodes (they are now dead code).
4. Keep the AST as the primary symbolic/serializable form.
5. Leave the code base smaller, easier to maintain, and aligned with the "AST primary + direct lowering" direction.
6. Preserve the ability to run the full test suite with zero failures at every step.

## Non-Goals (for this plan)

- Keeping a primitive form purely as an "export" or portable IR (would require a new decision).
- Re-implementing every last historical test that only exercised old reconstruction behavior.
- Adding a new canonical IR layer (that direction was already superseded).

## Current State (as of 2026-07-07)

- `Interpreter.Compile` → `DirectVmAbiEmitter`.
- `ExpansionPass` is gutted / no-op.
- Many old VM files (`ProgramCompiler.cs`, `RingAllocator.cs`, `ExceptionTableBuilder.cs`, etc.) already deleted from `Interpretation/Vm/`.
- `Node.IVisitor<TResult>` + `Accept` hook added in `Syntax/Node.cs` (foundation to eventually eliminate the dispatch switch).
- Direct emitter covers a large and growing set of node types (arithmetic, bitwise, control flow, closures, allocations, basic switch/using, labeled constructs, etc.).
- Multiple primitive-specific test files have been reduced to stubs.
- `ToPrimitives` virtual method + ~60+ overrides still exist as dead code.
- `Poly/Syntax/Primitives/` directory still contains the old definitions and expansion support types.
- All tests currently pass when running the suite.

## Phased Plan

### Phase 0: Audit & Preparation
- [ ] P0-01: Final audit — confirm no critical code (Interpreter, DomainModeling consumers, main execution) still transitively requires `ToPrimitives`, `ExpansionPass`, `ProgramCompiler`, or `PrimitiveExpansionMetadata`.
- [ ] P0-02: Inventory — list every file that still mentions `PrimitiveNode`, `ToPrimitives`, `Expansion*`, old `ProgramCompiler`, etc.
- [ ] P0-03: Confirm DirectVmAbiEmitter has no remaining `NotSupportedException` for nodes that appear in normal analyzed programs.
- [ ] P0-04: Capture baseline (build + full test run + list of files under `Syntax/Primitives` and `Nodes/*ToPrimitives`).

### Phase 1: Complete Direct Coverage (do not delete until this is green)
- [ ] P1-01: Identify any still-unsupported executable node types in `DirectVmAbiEmitter`.
- [ ] P1-02: Implement missing `Emit*` methods (full `SwitchStatement` pattern matching, richer `New` with constructors, complete `Invoke` targets, `StridedSetBits`, etc.).
- [ ] P1-03: Add necessary support in `AbiCtx` (named labels, better ring handling for loops, etc.).
- [ ] P1-04: Add focused tests (behavior + `DumpTree` format checks) for newly supported nodes.
- [ ] P1-05: Run full test suite and confirm zero failures on the direct path.

### Phase 2: Remove ToPrimitives from the AST
- [ ] P2-01: Remove the virtual `ToPrimitives` method (and its legacy comment) from `Poly/Syntax/Node.cs`.
- [ ] P2-02: Delete every `override IEnumerable<PrimitiveNode> ToPrimitives(...)` implementation in all concrete node files (`Add.cs`, `Lambda.cs`, `TryCatchFinally.cs`, `ForEachLoop.cs`, `Member.cs`, `UsingStatement.cs`, etc. — approximately 60+ locations).
- [ ] P2-03: Remove now-unused `using Poly.Syntax.Primitives;` directives from node files.
- [ ] P2-04: Delete any small helper types that only existed to support the removed methods.

### Phase 3: Prune the Primitives Layer
- [ ] P3-01: Delete the contents of (or the entire) `Poly/Syntax/Primitives/` directory:
  - `PrimitiveNode.cs`
  - `Primitives.cs` (main definitions)
  - `Label.cs`, `Phi.cs`, `TypeCheck.cs`, `OpKinds.cs`
  - `ExpansionContext.cs`, `ExpansionEnvironment.cs`
  - `PendingFunction` record and related
  - `README.md`
- [ ] P3-02: Delete `ExpansionPass.cs` (and the `PrimitiveExpansionMetadata` record) from `Interpretation/Analysis/`.
- [ ] P3-03: Delete any remaining reconstruction files that may still linger (`ProgramCompiler`, `RingAllocator`, `PrimitiveLinker`, `ExceptionTableBuilder`, etc.).
- [ ] P3-04: Remove `CompilationMode` values or the enum itself if they are no longer used by the direct path.

### Phase 4: Clean Core Execution Path
- [ ] P4-01: Remove `CompileViaPrimitives`, `CompileCoreLegacy`, and all related obsolete code + comments from `Interpreter.cs`.
- [ ] P4-02: Clean comments and dead references inside `DirectVmAbiEmitter.cs` (the "ToPrimitives → RingAllocator → ProgramCompiler" pipeline text).
- [ ] P4-03: Remove any remaining references in `DomainModeling`, `Introspection`, or other modules (usually only comments at this point).
- [ ] P4-04: Clean up `using` statements and any dead `CompilationMode` usage.

### Phase 5: Tests, Helpers, and Non-Critical Code
- [ ] P5-01: Audit remaining test files that still import or exercise the old path.
- [ ] P5-02: Either convert them to direct-only usage (preferred) or delete the files if they only tested reconstruction/expansion artifacts.
- [ ] P5-03: Update or remove helpers (`NodeTestHelpers.CompileWithPrimitives`, `UsePrimitiveExpansion`, etc.).
- [ ] P5-04: Prune or stub `VmCorrectnessTests`, `ExpansionIntegrationTests`, `PrimitiveExpand*Tests`, `ExceptionRegionTableTests`, etc. as appropriate.

### Phase 6: Documentation & Architecture Updates
- [ ] P6-01: Update `docs/decisions/2026-07-04-primitives-as-canonical-ir.md` (record that the decision is superseded for the VM execution path).
- [ ] P6-02: Update `docs/plans/archive/interpretation/interpretation-system-resolution-plan.md` (archived — no longer required).
- [ ] P6-03: Update `Poly/Interpretation/Vm/README.md` and `Poly/Interpretation/README.md`.
- [ ] P6-04: Delete `Poly/Syntax/Primitives/README.md`.
- [ ] P6-05: Review and update `AGENTS.md` (Placement Rules, build notes, etc.).
- [ ] P6-06: Clean any other docs, perf-comparison examples, or file-based `.cs` scripts that reference the old pipeline.

### Phase 7: Final Verification & Polish
- [ ] P7-01: Clean build (`dotnet build Poly.Tests/Poly.Tests.csproj`).
- [ ] P7-02: Full test run (`dotnet run --project Poly.Tests/Poly.Tests.csproj`) — must be 100% green.
- [ ] P7-03: Remove any remaining `using Poly.Syntax.Primitives;` that are now unused.
- [ ] P7-04: Delete empty directories.
- [ ] P7-05: Verify module boundaries (Syntax must not depend on Interpretation).
- [ ] P7-06: (Recommended) Continue migrating dispatch inside `DirectVmAbiEmitter` to the `Node.IVisitor` / `Accept` pattern.
- [ ] P7-07: Consider whether any "export" or diagnostic primitive form is still desired. If not, confirm complete removal.
- [ ] P7-08: Write a clear commit message and/or update any changelog.

## Exit Criteria

- No `ToPrimitives` method or override remains in the source tree.
- `Poly/Syntax/Primitives/` directory no longer exists (or is empty and will be deleted).
- `ExpansionPass` and related expansion types no longer exist.
- `Interpreter.Compile` and all normal execution paths use only direct lowering.
- Full test suite passes with zero failures.
- Documentation accurately reflects that primitives are removed from the VM path.
- No critical path depends on the deleted code.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Some obscure node type is still not implemented in direct | Complete Phase 1 before any deletion. |
| Tests that only exercised old reconstruction break | Convert or prune in Phase 5; use `DumpTree` + behavioral checks on direct path. |
| Documentation drift | Treat P6 and P7 as mandatory. |
| Someone still wants a primitive export form later | Document the decision; a future ADR can re-introduce a thin export layer if needed. |

## Suggested Ordering

1. Phase 0 (audit)
2. Phase 1 (coverage)
3. Phases 2–3 (the actual deletions — can be done in one or two PRs)
4. Phases 4–6 (cleanup)
5. Phase 7 (verification loop)

Parallelizable work: documentation updates and some test pruning can start early once coverage is solid.

## Related Work

- Visitor pattern foundation already exists (`Node.IVisitor` + `Accept`) — Phase 7 includes the opportunity to finish migrating the emitter dispatch.
- Many files were already pruned in prior steps (old `ProgramCompiler`, several test files, etc.). This plan finishes the job.

---

**Next step:** When ready, start with **P0-01 / P0-02** (audit) and report findings before touching deletion tasks.