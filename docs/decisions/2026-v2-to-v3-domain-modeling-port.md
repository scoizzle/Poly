# ADR / Planning Document: V2 → V3 Domain Modeling Port (Immutable Core + Preserved Evolution)

**Date:** 2026-05-31 (initial)  
**Status:** Living Plan  
**Owner:** Primary author  
**Related Decisions:**
- `2026-core-engineering-principles.md` (foundational)
- `2026-05-31-immutable-core-domain-modeling.md` (the strategic decision)
- `2026-05-31-evolution-layer-design.md` (the technical approach for the evolution layer)

## Context

The repository currently has two parallel domain modeling systems:

- **V2** (`Poly/Data/Modeling`): The current integrated, production surface. Uses internally mutable records + a large Command/Intent transactional mutation subsystem. Delivers strong value for MCP/LLM agents today via `ApplyWithTrace`, sessions, rich traces, and automatic rollback on analysis errors. Contains the full feature set, analyzers, lowering (including exact contract interface rules), demos, tests, recipes, and MCP tools.

- **V3** (`Poly/DomainModeling`): A cleaner foundation using true immutable records + a unified `DomainExpression` system + fluent builders. Reuses the shared `Syntax/Analysis` infrastructure. Currently has limited effects and early analysis, but a strong design sketch for the thin evolution layer (the primary agent interaction model). The fluent builders exist mainly for human test fixtures and occasional one-shot construction; they are not expected to be the dominant interface for LLM/MCP-driven work.

**Why this port now:**
- Zero customers = low-risk window for a major architectural shift.
- Two cost-benefit analyses + explicit decision confirmed that the V2 mutation layer creates unsustainable incidental complexity ("mutation tax") as the system grows.
- The immutable core + `DomainExpression` align better with the project's core engineering principles (see `2026-core-engineering-principles.md`). The fluent builders are a secondary ergonomic convenience rather than a primary value driver.
- The **non-negotiable constraint** is preserving the transactional, correctness-gated evolution experience for future agent consumers.

**Important recent context (as of late May 2026):**
- We have centralized high-level decisions in `docs/decisions/`.
- `AGENTS.md` has been strengthened with explicit instructions to check corresponding decisions in `docs/decisions/` before analysis or changes to any section.
- The six Core Engineering Principles are deliberately kept prominent in `AGENTS.md` (not buried only in a decision file) because agents weight AGENTS.md more reliably than general decision documents.

This port must be executed in a way that respects those documentation and agent-instruction realities.

## Goals

- Make the **immutable V3 model** the single source of truth.
- Deliver a **thin but highly ergonomic evolution layer** that provides an excellent agent experience for both construction and change, while preserving full `ApplyWithTrace`-style semantics, rich traces, rollback on analysis errors, and MCP compatibility — all with significantly lower long-term maintenance cost than the old V2 mutation machinery.
- Achieve **full observable parity** for consumers during transition, while treating the MCP tool surface and interaction model as something we will deliberately optimize for how models actually use tools (both incremental improvements and more fundamental redesign).
- Design the Evolution layer with a full real-time visual authoring experience in mind from the start. This includes:
  - Live rendering of LLM-driven changes.
  - Direct human editing of the domain through UI controls (adding/editing entities, properties, actions, effects, policies, stages, relationships, etc.).
  - Optimistic application of changes with reconciliation against analysis results.
  - Fine-grained, observable, incremental change events suitable for visual diffing and live updates.
  - Strong support for stable visual identity (NodeId continuity) and rich history/branching.

  This is a major long-term requirement, not an afterthought (see the Evolution Layer design doc for the full set of UI requirements).
- Use the documented roadblocks as forcing functions to prove the new foundation is superior.
- Significantly reduce the mutation tax while strengthening the "domain model as the key artifact" principle.
- Keep all work aligned with the Core Engineering Principles and the requirement to consult `docs/decisions/` first.

**Explicit non-goals for this phase:**
- Full runtime instance execution/simulation engine.
- Visual authoring.
- Keeping both implementations indefinitely.

## Recommended Approach

**"Immutable Core First + Thin Evolution Layer + Staged Migration + Documentation Discipline"**

Phases (refined from the earlier detailed plan):

### Phase 1: Foundation + Minimal Viable Evolution Layer
- Implement the evolution layer (see `2026-05-31-evolution-layer-design.md`).
- **High priority in Phase 1:** Design and prototype a fluent, ergonomic public API for the evolution layer itself. The explicit goal is to make the transactional evolution path the primary, pleasant interface for both construction and change — potentially making heavy further investment in the separate fluent builder API unnecessary.
- Use V3 builders (or a thin pure construction abstraction) only as an implementation detail inside the applicator when convenient. The builders are now viewed as a secondary ergonomic convenience (mainly useful for human test code), not a co-equal primary deliverable.
- Support a useful subset of operations via the existing `DomainMutationIntent` shape (for MCP compatibility) or a cleaner native change model.
- Prove NodeId continuity and rich trace generation.
- Prove on PersonLifecycle examples + at least one roadblock scenario, preferably using the new fluent evolution surface.
- Ensure the layer produces traces and rollback behavior that MCP agents can consume without behavior change.

**Documentation requirement for this phase:**
- Any new significant design choices during implementation must result in (or update) a decision record in `docs/decisions/`.
- Update `AGENTS.md` if operational rules change.

### Phase 2: Analysis & Lowering Parity
- Port critical metadata (Effective* etc.) using the shared Syntax.Analysis pipeline.
- Ensure lowering produces identical contract interfaces (per the authoritative rules in AGENTS.md).
- Do not duplicate analyzer logic unnecessarily.

**Documentation requirement:**
- The contract interface rules and their rationale should have (or be linked from) a clear decision record.

### Phase 3: Consumer Migration (MCP-first)
- Migrate MCP tools to the new evolution layer (initially with minimal visible change for continuity).
- Evolve the MCP tool surface and interaction patterns to be optimized for how models actually use tools (scope includes both incremental improvements and more fundamental redesign of the agent-facing catalog, batching, affordances, and feedback loops).
- Migrate demos (the evolution layer + intent compatibility is the primary path; builders can be used opportunistically for initial construction of test domains if helpful).
- Migrate tests.

### Phase 4: Full Expressiveness + Roadblock Resolution
- Fill gaps in effects, Actor, subscriptions, etc., using the immutable + expression model.
- Explicitly solve the known roadblocks on the new foundation.

### Phase 5: Cutover & Cleanup
- Switch integrated surfaces to V3 + evolution layer.
- Remove the old mutable V2 implementation.
- Update all documentation (including ensuring the principles decision and this port plan stay current).

## Key Constraints & Realities (Updated)

- **Agent visibility is not equal**: Core principles and high-frequency operational rules must stay prominent in `AGENTS.md`. Deeper rationale belongs in `docs/decisions/`.
- Before any significant work in a section, the corresponding decision(s) in `docs/decisions/` must be reviewed (enforced via AGENTS.md).
- Preserve exact lowering contract interface behavior (this is a hard requirement from AGENTS.md).
- Dual maintenance is acceptable but should be time-boxed with clear gates.

## Next Planning Steps (Recommended)

1. **Refine Phase 1 scope** — Decide the exact minimal set of operations to support in the first evolution layer milestone (see the companion breakdown in `docs/plans/v2-to-v3-domain-modeling-port-roadmap.md`).
2. Create supporting decision records as design choices are made during Phase 1 (especially NodeId continuity and change representation strategy).
3. Define measurable "Phase 1 complete" criteria that include:
   - Working evolution layer with good traces + rollback
   - Proof on PersonLifecycle + at least one roadblock
   - Documentation and `AGENTS.md` updates
   - Clear go/no-go for Phase 2
4. Continue treating this document as a living artifact.

**Related living documents (execution side):**
- `docs/plans/v2-to-v3/master-roadmap.md` — Current master coordination document.
- `docs/plans/v2-to-v3/orchestration-guide.md` — **The authoritative guide for how the entire multi-agent effort (large + small models) is orchestrated and coordinated.**
- `docs/plans/v2-to-v3/agent-summaries/` — The mechanism by which all agents (especially smaller ones) report completed work. Orchestrators consume these summaries to keep the master plan accurate.
- `docs/plans/v2-to-v3/workstreams/` — Detailed workstream task files.
- `docs/plans/v2-to-v3/simple-agent-tasks/` — Micro-tasks designed so smaller/simpler/cheaper model agents can handle the majority of implementation work.

**Related decisions:**
- `docs/decisions/2026-core-engineering-principles.md`
- `docs/decisions/2026-05-31-evolution-layer-design.md`

This plan and the planning artifacts under `docs/plans/v2-to-v3/` are living. Update them as work progresses.

---

**Status of this document**: Initial repo-resident version based on the approved internal plan + post-approval documentation work (May/June 2026).

---

## Code Review Notes (Added 2026-05-30)

The following review was performed against the actual V2 (`Poly/Data/Modeling`) and V3 (`Poly/DomainModeling`) codebases. These notes identify gaps between the plan's assumptions and the code's reality. A follow-up agent should review and decide which to apply.

### 1. V3 expressiveness gaps are not catalogued — this will block Phase 4

The plan references "roadblocks" abstractly but doesn't enumerate what V3 cannot currently model that V2 can. Specific gaps found in the code:

- **Entity inheritance**: V2 `Entity.ParentEntity` doesn't exist in V3.
- **Event subscriptions with correlation**: V2 `EventSubscription` + `EventCorrelationBinding` has no V3 equivalent.
- **Relationship-scoped stages/policies**: V2 `Relationship` carries stages, policies, and properties independently of entities. V3 `Relationship` has only properties and type references — no stages or policies.
- **Rule-based policies**: V2 `Policy` has a `Rules` collection with subtypes (`CrossPropertyRule`, `ActorTypeRule`, etc.). V3 `Policy` uses `DomainExpression` only — cleaner but a breaking simplification.
- **Actor subtype**: V2 has `Actor` as a first-class `Entity` subtype. V3 has no `Actor` type.

**Recommendation**: Add a catalog of V3 expressiveness gaps (as a new decision record or appendix) before Phase 2, so Phase 4 scope is predictable rather than discovered during execution.

### 2. Analysis parity is undercounted — Phase 2 is multiple sub-phases

V2 has 10 specialized analyzers registered in `Poly/Data/Modeling/Analysis/DomainModelAnalyzer.cs:27-37`:
`StructuralDomainAnalyzer`, `SemanticDomainAnalyzer`, `PolicyConstraintAnalyzer`, `EffectAnalyzer`, `CapabilityAnalyzer`, `ConstraintPropagationAnalyzer`, `EnumConstraintSubsetAnalyzer`, `ActionEventQualityAnalyzer`, `ConstraintQualityAnalyzer`, `ContractIntegrationAnalyzer`.

V3 (`Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs:7-9`) registers only the first two. Porting 8 analyzers while maintaining exact diagnostic parity is not a single phase. Split Phase 2 into 2a (structural + semantic + effect + policy — the core path for PersonLifecycle) and 2b (remaining 4 analyzers).

### 3. Builders should not be deprioritized until the evolution layer matches their ergonomics

The plan (Section "Recommended Approach" > Phase 1, bullet 2) says "the dedicated fluent builder API [can become] unnecessary." In the current codebase:
- The V3 builders (`Poly/DomainModeling/Builders/`) work today. `PersonLifecycleViaBuilders.cs` builds a complete domain.
- The evolution layer's `DomainEvolution.ApplyChanges()` (`Poly/DomainModeling/Evolution/DomainEvolution.cs:64`) is `return current` — a no-op.
- There are zero concrete `DomainChange` subclasses.

Deprioritizing builders before the evolution layer exists is putting the cart before the horse. **Recommendation**: Explicitly state that builders remain the supported V3 construction path through Phase 1, and the evolution layer targets parity by Phase 1 exit. The deprioritization question should be revisited only after the evolution layer is proven on PersonLifecycle.

### 4. The "thin evolution layer" language undercounts the applicator scope

The plan characterizes the evolution layer as "thin," but the applicator must still handle: node copying with preserved NodeIds, collection-level add/remove/replace semantics, nested structural updates, trace generation per change step, and analysis gating. While V3 removes the Apply/Rollback command pairs and lock, it replaces them with a functional transformation pipeline that still has meaningful surface area. The language should shift from "thin" to "no unnecessary ceremony" to set accurate expectations.