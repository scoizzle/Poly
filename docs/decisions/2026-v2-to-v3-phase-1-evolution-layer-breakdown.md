# Phase 1 Task Breakdown: Evolution Layer (Minimal Viable)

**Parent Plan:** `2026-v2-to-v3-domain-modeling-port.md`  
**Related:** `2026-05-31-evolution-layer-design.md`

## Goal for Phase 1

Have a working, thin evolution layer on the immutable V3 core that:
- Supports a useful subset of operations.
- Produces correct new immutable roots.
- Delivers rich traces + automatic rollback on analysis errors.
- Is compatible enough with existing `DomainMutationIntent` shape for early MCP/agent use.
- Is proven on the PersonLifecycle examples + at least one real roadblock scenario.

## High-Level Workstreams

### 1. Core Evolution Layer Implementation
- Design and implement the simplified `DomainEvolution` (Apply batch + `Evolve()` fluent builder), `EvolutionResult`, `EvolutionTrace`. (The explicit `EvolutionTransaction` / `BeginTransaction` / `Commit` model was removed — see 2026-05-31-evolution-layer-design.md Open Question #8 resolution.)
- Implement a `DomainChange` (or intent adapter) representation.
- Build the applicator that interprets changes and produces new immutable `Domain` instances (using V3 builders or a thin pure construction path as the mechanism). Note: Builders are a secondary convenience; the evolution/intent surface is expected to be the primary agent interaction model.
- **High priority:** Design and prototype a fluent, ergonomic public API on top of the simplified evolution core (see `spikes/fluent-evolution-api-proposal.md`). The goal is to make `evolution.Evolve().AddX()... .Apply()` feel as nice as (or nicer than) the current builder syntax for agent use, so the dedicated fluent builders can be deprioritized.
- Implement NodeId continuity strategy for unchanged subtrees.
- Wire analysis gate + rollback logic.
- Generate rich traces (steps, affected nodes, timing, success/rollback status).
- Design the change model and observation points to support a full real-time visual authoring experience, including:
  - Fine-grained, observable, incremental change events (not just post-`Commit()` traces).
  - Direct human-driven changes from UI controls.
  - Optimistic application + reconciliation patterns.
  - Strong NodeId stability for visual identity.
- Explicitly avoid the anti-patterns listed in the Evolution Layer design doc (opaque changes, batch-only design, weak NodeId stability, over-reliance on old intent model, etc.).

**Key open technical questions to resolve in this workstream:**
- Exact shape of the change/intent adapter for the first milestone.
- How deep to go on "with" helpers / pure construction helpers.
- Whether a fluent "EvolutionBuilder"-style API on top of the transactional layer can be made ergonomic enough to significantly reduce or eliminate the need for the separate fluent builder surface.
- Trace fidelity target vs. current V2 traces.
- What level of fine-grained change observation the Evolution layer must expose to support live visual authoring UIs (this is now a major additional requirement).

### 2. Initial Operation Support (MVP Scope)
Decide and implement support for a minimal but useful set, for example:
- Basic type add/remove (Entity, Primitive, etc.)
- Property add/remove
- Stage add/remove + basic hierarchy
- Action add/remove + simple effects (Create, Publish, Transition)
- Policy attachment via `DomainExpression`

**Success gate:** Can perform a multi-step batch that includes one intentional error (analysis should catch it and rollback cleanly with good diagnostics).

### 3. NodeId Continuity & Incremental Analysis
- Define and implement strategy for preserving stable `Node.Id` values when producing new immutable versions.
- Ensure V3 analyzers continue to work well with the new roots.
- Validate incremental analysis behavior across an evolution.

### 4. Trace & Diagnostics Quality
- Ensure `EvolutionTrace` (and the DTOs consumed by MCP) are useful for agents.
- Prove good error messages and rollback behavior.

### 5. Proof on Living Examples
- Get `PersonLifecycleViaBuilders` + `PersonLifecycleExample` fully working through the new evolution layer.
- Implement at least one documented roadblock scenario cleanly (e.g., Library RenewLoan with dynamic calculation via `DomainExpression`).

### 6. Documentation & Agent Instructions (Mandatory per project rules)
- Create or update decision record(s) for any significant design choices made during implementation (especially NodeId strategy and change representation).
- Update `AGENTS.md` if any new operational rules emerge.
- Ensure the new evolution layer code and usage examples follow the Core Engineering Principles.

### 7. Early Integration / Compatibility
- Create a thin adapter so that old `CreateMutation()` call sites can continue to work (for dual maintenance period).
- Basic smoke test with one MCP tool path (even if simulated).

### 8. Fluent Evolution API Surface (Top-Tier Priority for Phase 1)
This is now viewed as one of the most important deliverables of Phase 1.

The goal is to design and implement a fluent, ergonomic public API for the evolution layer that can serve as the primary interface for both construction and change — potentially making heavy investment in the separate fluent builder API unnecessary.

- Design a fluent "EvolutionBuilder"-style surface on top of the core transactional machinery (see `spikes/fluent-evolution-api-proposal.md`).
- Create dedicated micro-tasks for this work.
- Prototype it against real usage (slices of the ECommerce/Library domains or PersonLifecycle examples).
- Explicitly evaluate whether success here allows us to treat the existing fluent builders as a secondary convenience (mainly for human test fixtures) rather than a co-equal deliverable.
- The fluent evolution path must still deliver excellent traces, diagnostics, and rollback behavior.

## Suggested Sequencing

1. Evolution layer skeleton on the simplified model (DomainEvolution with Apply + Evolve() builder, basic applicator, trace/result records). The old transaction/commit shape is no longer used.
2. Design the fluent evolution API shape in parallel (the primary ergonomic surface).
3. NodeId continuity mechanism.
4. Real change applicator + analysis gate (success + rolled-back paths).
5. Implement the fluent Evolve() surface on top of the core.
6. First operations exposed through the fluent surface.
7. Prove on PersonLifecycle + one roadblock scenario.
8. Early MCP/agent compatibility smoke test.
9. Documentation + decision records (including the resolved decision to drop the transaction model).
10. Evaluate the future scope of the separate (non-evolution) fluent builder API.

## Exit Criteria for Phase 1

- Clean build.
- Evolution layer can perform non-trivial batches with correct rollback on analysis errors.
- Rich, usable traces.
- At least one roadblock scenario works cleanly on the new foundation.
- Relevant decision records created/updated.
- `AGENTS.md` references are current.
- Clear "ready for Phase 2" checklist defined.

---

This is intended as a living task breakdown. It should be updated as we learn during actual implementation.