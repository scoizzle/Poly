# Workstream WS1 (Consolidated): Evolution Layer Applicator + MVP Operations + NodeId Continuity

**Phase**: 1  
**Priority**: Critical Path (highest leverage)  
**Owner**: Grok (primary orchestrator)  
**Status**: Foundation Complete (MVP operations + core applicator + basic traces delivered). Phase 1 continues with quality/audit/proof focus per re-evaluated master roadmap (June 2026).  
**Last Updated**: 2026-06 (under active ownership)

## Goal

Deliver a **working** thin evolution layer on the V3 immutable core that actually changes models:

- Real `DomainChange` subtypes (not just the abstract base).
- An applicator that interprets changes to produce new immutable `Domain` roots (using V3 builders internally where practical).
- NodeId preservation on unchanged subtrees (inline, mechanical).
- The full analysis gate + `EvolutionResult` contract (success with new root, or rolled-back with original root + diagnostics + rich trace).
- The first 6–8 MVP operations needed for PersonLifecycle + one roadblock scenario.
- Clean, tested, principle-aligned implementation.

This is the single most important deliverable in Phase 1. Everything else (traces, proofs, MCP compatibility, future UI) depends on it working.

## Rationale for Consolidation (from Code Review + Ownership Plan)

- The skeleton (`DomainEvolution`, `EvolutionResult`, `EvolutionTrace`, `EvolutionBuilder`, abstract `DomainChange`) already exists and compiles.
- `ApplyChanges` is a deliberate identity placeholder.
- WS1 (core infrastructure) and WS3 (operations) are not separable — the applicator *is* the operations.
- NodeId continuity is a mechanical detail inside the applicator, not a standalone research workstream.
- Merging removes fake handoffs and matches ground truth.

See the 2026 ownership plan and the code review notes in `master-roadmap.md` for the full history.

## Entry Criteria (Satisfied)

- V3 immutable core + builders are stable and can construct PersonLifecycle.
- Relevant decisions reviewed (immutable core, evolution-layer-design including the "no explicit transaction" resolution, core principles).
- Skeleton files exist in `Poly/DomainModeling/Evolution/`.

## MVP Operation Scope (Tight – First Milestone Only)

Focus exclusively on what is required to construct/evolve PersonLifecycle + one roadblock:

- Add/Remove: Entity, PrimitiveType, Event, ValueType
- Add/Remove Property on Entity
- Add/Remove Stage (basic + simple parent)
- Add/Remove Action on Entity/Stage (parameters + DomainExpression policies)
- Add/Remove simple Effects: CreateEntityInstance (with PropertyBindings), PublishEventEffect, StageTransitionEffect
- Attach Policy (DomainExpression form)
- Basic Relationship add/remove

**Explicitly out of scope for this milestone** (defer or push to later Phase 1):
- Composite/Conditional/InvokeAction effects
- Actor identity/claims configuration
- Event subscriptions + correlation bindings
- Relationship-scoped stages/policies
- Imported contracts
- Full fluent `Evolve().AddEntity(...)` ergonomics (generic `Apply(DomainChange)` is sufficient initially)

Additions to this list require explicit justification against a real first consumer + principle check.

## Key Work (Prioritized Order)

1. **Decision record**: Phase 1 Change Representation Strategy (native `DomainChange` records + thin intent adapter for MCP compatibility; builders as primary construction path inside applicator).

2. **Concrete `DomainChange` subtypes** (first 5–6):
   - `AddEntityChange`, `RemoveEntityChange`
   - `AddPropertyChange`, `RemovePropertyChange`
   - `AddStageChange`, `RemoveStageChange`
   - `AddActionChange`, `RemoveActionChange`
   - `AddEffectChange`, `AttachPolicyChange` (etc.)

3. **Applicator implementation**:
   - `ApplyChanges(Domain current, IReadOnlyList<DomainChange>)` that produces a new immutable root.
   - Primary path: use existing V3 `DomainBuilder` / `*Builder` classes internally for construction.
   - NodeId preservation: unchanged subtrees keep their original `Node.Id` (simple `with { Id = original.Id }` or builder equivalent).
   - New nodes receive fresh `NodeId.NewId()`.

4. **Wire the full gate**:
   - `DomainEvolution.Apply(...)` already has the analysis + rollback shape — make it call real applicator.
   - Prove both success (new root) and error (original root + `WasRolledBack = true`) paths.

5. **Trace generation** (basic but useful):
   - Per-change step descriptions + affected NodeIds (once NodeId preservation works).
   - Duration, error/warning counts.

6. **Tests** (alongside every increment):
   - Single-operation success.
   - Multi-step batch.
   - Intentional analysis error → clean rollback with original root.
   - NodeId continuity test (unchanged subtree retains Id across evolution).

7. **MVP operations completion** + integration with WS4 (traces) and WS5 (proof).

## NodeId Continuity Approach (Inline)

- Mechanical and cheap: when the applicator copies or reuses a subtree that was not modified by the current change batch, preserve the original `.Id`.
- Use the fact that all model types derive from `Node` (which has `public NodeId Id { get; init; }`).
- No separate "research" phase. Implement inside the applicator from the first change type.
- Prove with a test that runs analysis incrementally using the prior `AnalysisResult` + affected nodes.

## Anti-Patterns to Avoid (Hard Constraints from Evolution Design Doc)

- Do not make `DomainChange` subtypes opaque or stringly-typed.
- Do not design only for coarse batch use — the model must support future fine-grained observation.
- Weak NodeId stability is unacceptable (it breaks incremental analysis and future visual identity).
- Do not hide all mutation behind the old fluent builders in a way that makes direct change application second-class.

## Exit Criteria for This Workstream (Measurable)

- At least 6 MVP operations work end-to-end through `new DomainEvolution(d).Apply(changes)` and the lightweight `EvolutionBuilder`.
- A multi-step batch containing one intentional analysis error returns the *original* root + `WasRolledBack = true` + diagnostics + trace.
- NodeIds are preserved on unchanged subtrees; a test demonstrates incremental analysis still functions.
- Traces contain usable per-step information (change type + affected NodeIds).
- Clean build + all new tests passing.
- PersonLifecycle slice can be constructed via the evolution layer (not just raw builders).
- Decision record for change representation exists and is referenced from the roadmap.
- Interfaces are stable enough for WS4 and WS5 to proceed in parallel.

## Dependencies & Coordination

- This workstream is the source of truth for the `DomainChange` shape and applicator contract.
- WS4 and WS5 must coordinate on the `EvolutionTrace` and result shapes via this owner.
- Any change to the public `DomainEvolution` / `EvolutionResult` / `EvolutionTrace` API must be reviewed here first.

## Verification

- Every micro-task must leave `dotnet build` green and add/run relevant tests.
- WS1 owner personally reviews every agent summary + diff before acceptance.
- No "done" while the evolution layer is still a no-op on real changes.

## Related Documents

- `docs/decisions/2026-05-31-evolution-layer-design.md` (the authoritative design, including UI requirements and anti-patterns)
- `docs/decisions/2026-core-engineering-principles.md`
- 2026 ownership plan (session plan file)
- Old `ws1-evolution-layer-core.md` and `ws3-mvp-operations.md` (historical — do not edit)

---

**Status**: This consolidated workstream is now the single source of truth for Phase 1 critical path execution under active ownership. All prior separate WS1/WS3/WS2 files are superseded for execution purposes.

**Re-evaluation Note (June 2026)**: WS1 delivered the critical foundation (thin evolution layer on immutable core, NodeId continuity, MVP operations, basic usable traces). It is treated as complete for handoff purposes, but Phase 1 priorities have been re-evaluated (see master-roadmap.md).

Remaining Phase 1 work has shifted to:
1. WS7 Expressiveness Audit (highest leverage)
2. Trace quality for agents (WS4)
3. Real proofs on documented scenarios + roadblocks (WS5)
4. Incremental analysis support
5. Roadmap hygiene

The original WS1 exit criteria were met for the foundation layer. Downstream workstreams now drive the rest of Phase 1.

Owner: Grok (orchestrator).