# V2 → V3 Domain Modeling Port — Roadmap & Task Tracker (Legacy / Redirect)

> **Note**: This file is being superseded by the new structured planning area.
> 
> **Please use instead**:
> - `docs/plans/v2-to-v3/master-roadmap.md` — Master coordination document
> - `docs/plans/v2-to-v3/workstreams/` — Individual workstream task files (designed for parallel agent work)

**Status**: Superseded (content migrated)  
**Last Updated**: 2026-06-01  

**Related Decisions**:
- `docs/decisions/2026-core-engineering-principles.md`
- `docs/decisions/2026-05-31-immutable-core-domain-modeling.md`
- `docs/decisions/2026-05-31-evolution-layer-design.md`

---

## Guiding Principles (from decisions)

Before doing any work on this roadmap, review:
- The six **Core Engineering Principles** (especially "build working code before abstraction" and "domain model is the key artifact").
- The requirement to consult the relevant decision(s) in `docs/decisions/` before making changes to any area.

---

## Overall Strategy

**Immutable Core + Thin Evolution Layer + Staged Migration + Documentation Discipline**

High-level phases:

1. **Foundation + Minimal Viable Evolution Layer** (highest priority)
2. **Analysis & Lowering Parity**
3. **Consumer Surface Migration** (MCP first)
4. **Full Expressiveness + Roadblock Resolution**
5. **Cutover & Cleanup**

---

## Phase 1: Foundation + Minimal Viable Evolution Layer

**Goal**: A working thin evolution layer on the immutable V3 core that supports useful operations, produces correct new roots, delivers rich traces + rollback on analysis errors, and is proven on real examples.

### 1.1 Evolution Layer Core
- [ ] Design and implement the simplified `DomainEvolution` (Apply + Evolve() fluent builder), `EvolutionResult`, `EvolutionTrace` (explicit EvolutionTransaction removed per 2026-05-31 decision)
- [ ] Define initial `DomainChange` / intent adapter shape
- [ ] Build applicator that produces new immutable `Domain` instances (primary path = V3 builders)
- [ ] Implement NodeId continuity strategy for unchanged subtrees
- [ ] Wire analysis gate + automatic rollback on error diagnostics
- [ ] Generate usable `EvolutionTrace` (steps, affected nodes, timing, rollback status)

**Status**: Not started  
**Key open questions**:
- How deep should "with" helpers go vs. always routing through builders?
- Target fidelity for traces vs. current V2 traces.

### 1.2 Initial Operation Support (MVP Scope)
Decide and implement a minimal useful set. Proposed starting scope:
- [ ] Basic type lifecycle (add/remove Entity, Primitive, Event, etc.)
- [ ] Property add/remove
- [ ] Stage add/remove + simple hierarchy
- [ ] Action add/remove + simple effects (CreateEntityInstance with bindings, Publish, StageTransition)
- [ ] Policy attachment using `DomainExpression`

**Status**: Not started  
**Success gate**: Can perform a multi-step batch containing one intentional analysis error that triggers clean rollback with good diagnostics.

### 1.3 NodeId Continuity & Analysis
- [ ] Finalize and implement NodeId preservation strategy
- [ ] Validate that V3 analyzers work correctly against evolved roots
- [ ] Test incremental analysis behavior across evolutions

**Status**: Not started

### 1.4 Trace & Agent Experience Quality
- [ ] Ensure traces are useful for LLM/MCP agents
- [ ] Validate rollback behavior and error messaging
- [ ] Create basic compatibility adapter so old `CreateMutation()` call sites can continue working during transition

**Status**: Not started

### 1.5 Proof on Living Specs
- [ ] Get `PersonLifecycleViaBuilders` + `PersonLifecycleExample` fully working through the evolution layer
- [ ] Cleanly implement at least one documented roadblock scenario (e.g. Library `RenewLoan` with dynamic calculation via `DomainExpression`)

**Status**: Not started

### 1.6 Documentation & Process Discipline (Mandatory)
- [ ] Create/update decision record(s) for significant design choices made in this phase (especially NodeId strategy and change representation)
- [ ] Update `AGENTS.md` if any new operational rules emerge
- [ ] Ensure all new code and examples follow the Core Engineering Principles

**Status**: Not started

### 1.7 Early Integration Smoke Test
- [ ] Basic smoke test of one MCP tool path using the new layer (even if simulated at first)

**Status**: Not started

---

## Phase 1 Exit Criteria

- Clean builds across the solution.
- Working evolution layer with correct rollback on analysis errors.
- Rich, usable traces for agents.
- Proof on PersonLifecycle examples + at least one roadblock scenario.
- Relevant decision records created/updated.
- `AGENTS.md` references are current.
- Clear go/no-go criteria defined for Phase 2.

---

## Later Phases (High Level)

**Phase 2**: Analysis & Lowering Parity  
**Phase 3**: Consumer Migration (MCP first, then demos/tests)  
**Phase 4**: Full Expressiveness + Roadblock Resolution  
**Phase 5**: Cutover & Removal of V2 mutable implementation

Detailed task breakdowns for these phases will be added as Phase 1 progresses.

---

## Notes

- This document is the working task tracker for the port.
- Keep it updated as work progresses.
- All major design decisions made while executing these tasks should result in (or update) a record in `docs/decisions/`.
- Before starting work on any significant item, confirm the relevant decision(s) have been reviewed.