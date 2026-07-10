# ADR / Planning Document: V2 → V3 Domain Modeling Port (Immutable Core + Preserved Evolution)

**Date:** 2026-05-31 (initial)  
**Status:** Living Plan — **July 2026: Phase 1 complete; V2 has zero product consumers; first consumer = MCP + direct domain API; then freeze/delete V2 (not live MCP migration / full parity)**  
**Owner:** Primary author  
**Execution plan:** `docs/plans/v2-to-v3/master-roadmap.md` (milestones) + **`docs/plans/v2-to-v3/v3-completion-plan.md`** (gaps + WP1–WP9 implementation order)  
**First consumer spike:** `docs/plans/v2-to-v3/spikes/first-v3-consumer.md`  
**MCP principles:** `docs/plans/v2-to-v3/spikes/mcp-guiding-principles.md`  
**Related Decisions:**
- `2026-core-engineering-principles.md` (foundational)
- `2026-05-31-immutable-core-domain-modeling.md` (the strategic decision)
- `2026-05-31-evolution-layer-design.md` (the technical approach for the evolution layer)
- `2026-06-08-vm-as-canonical-semantics.md` (execution target for lowered DomainExpression)
- `2026-06-08-domain-lowering-boundary.md` (DomainModeling lowers to generic AST only)

## Context

The repository has two parallel domain modeling systems:

- **V2** (`Poly/Data/Modeling`): Legacy mutable-core + large mutation/intent machinery. **As of July 2026 it has no product consumers** — only in-repo remnants (prototype MCP tools, demos, V2 tests). It is not a live agent surface that must be preserved for compatibility.

- **V3** (`Poly/DomainModeling`): Immutable records + `DomainExpression` + evolution layer (`Apply` / `Evolve`). **This is the only stack we invest in going forward.** Builders remain a convenience for tests/fixtures; evolution is the intended agent interaction model.

**Why this port now:**
- **Zero product consumers of V2** = still the low-risk window to finish cutover without migration theater.
- Two cost-benefit analyses + explicit decision confirmed that the V2 mutation layer creates unsustainable incidental complexity ("mutation tax").
- Immutable core + `DomainExpression` align with core engineering principles.
- **Non-negotiable for future agents:** analysis-gated evolution, rich diagnostics/traces, rollback-on-error semantics — implemented on V3, not by keeping V2 alive.

**Important recent context (as of late May 2026):**
- We have centralized high-level decisions in `docs/decisions/`.
- `AGENTS.md` has been strengthened with explicit instructions to check corresponding decisions in `docs/decisions/` before analysis or changes to any section.
- The six Core Engineering Principles are deliberately kept prominent in `AGENTS.md` (not buried only in a decision file) because agents weight AGENTS.md more reliably than general decision documents.

This port must be executed in a way that respects those documentation and agent-instruction realities.

## Goals

- Make the **immutable V3 model** the single source of truth.
- Deliver analysis-gated evolution (`Apply` / `Evolve`) with usable traces and rollback — the agent-facing contract for construction and change.
- **Ship the first real consumer on V3:** a **direct domain API** (evolve / query / optional lower+eval) with **MCP as a thin adapter** over it — **not** a compatibility migration of the V2-shaped `DomainTools` surface.
- **Freeze then delete V2** once that path works; dual maintenance is temporary waste, not a product strategy.
- Grow expressiveness **only when that path needs it** (roadblocks, UI, next agent scenario).
- Long-term: evolution supports real-time visual authoring (live LLM edits, human UI edits, optimistic apply, NodeId stability) — design constraint, not Phase 2 scope.
- Align with Core Engineering Principles; consult `docs/decisions/` before major changes.

### Quality bar for the cutover (July 2026)

| Focus | Practice |
|-------|----------|
| **System correctness** | Analysis gate, rollback, truthful diagnostics, correct VM evaluation when runtime truth is required. |
| **Robustness via composition** | Small composable ops on the direct API; multi-step via `Apply`. MCP **curates** agent-facing tools (see MCP principles spike). |
| **MCP + direct API guiding light** | MCP scenarios pull features; the direct API is the contract into DomainModeling / Syntax / VM. Tool design: `spikes/mcp-guiding-principles.md`. |
| **Tests** | Primary net on the direct API (TUnit); MCP smokes / agent-task evals reuse those scenarios. |
| **Natural-reading code** | Fluent, name-for-what-it-is surfaces; avoid pattern-taxonomy and V2 intent-bag shapes as defaults. |

**Explicit non-goals:**
- Full V2 feature parity before the MCP/direct path works.
- Long-lived V3→V2 adapters “for MCP continuity.”
- Full runtime instance simulation engine (until a consumer demands it).
- Keeping both implementations indefinitely.
- Domain logic living only inside MCP tool method bodies.

## Recommended Approach

**"Immutable Core + Evolution + Direct API + Thin MCP + Delete V2"**

(Not: full V2 parity, then migrate a live MCP surface.)

### Phase 1: Foundation + Minimal Viable Evolution Layer — **DONE**
- Evolution layer, fluent `Evolve()`, NodeId continuity, proofs, audit — see master roadmap.

### Phase 2: Capabilities the consumer needs
- Direct API composability + diagnostics quality as dogfood demands.
- E2E DomainExpression → VM when runtime evaluation is required.
- Contract/program generation only if tools emit code/interfaces.
- Not a V2 checklist — pull by MCP/direct scenarios.

### Phase 3: First V3 consumer + freeze V2 — **consumer named; implementation not started**
- Direct domain API + thin MCP **on V3 only** (`spikes/first-v3-consumer.md`).
- Free to redesign tool surface for how models use tools.
- Declare **V2 freeze**: no new V2 features.

### Phase 4: Expressiveness as pulled by consumers
- Actor, rule policies, roadblocks — only when the live MCP/direct path needs them.

### Phase 5: Delete V2
- Remove `Poly/Data/Modeling` and V2-only demos/tests/MCP code.
- Update docs / AGENTS.md.

## Key Constraints & Realities (Updated July 2026)

- **V2 has zero product consumers** — cutover risk is in-repo only; use that freedom.
- **Agent visibility is not equal**: principles stay in `AGENTS.md`; rationale in decisions.
- **Contract interface naming** (AGENTS.md) still applies when/if we generate interfaces — for the V3 consumer, not “parity with V2 files.”
- **Dual maintenance is not a goal** — freeze and delete V2 after M2 works.
- **No new V2 investment** except build-breaking fixes during transition.
- **MCP is not the domain layer** — it adapts the direct API.

## Status Snapshot (2026-07-10)

| Area | State |
|------|--------|
| Phase 1 evolution foundation | **Done** |
| Expressiveness audit (WS7) | **Done** (living) |
| DomainExpression → Syntax / VM ready enough | **Done** |
| V2 product consumers | **Zero** |
| First V3 consumer named | **MCP + direct domain API** |
| First V3 consumer implemented | **Not started** |
| V2 freeze / delete | **Not started** |

Do **not** restart Phase 1 greenfield evolution tasks. See `docs/plans/v2-to-v3/master-roadmap.md` for tasking.

## Next Planning Steps (Recommended)

1. ~~Name the first V3 consumer~~ → **Done**.
2. ~~MCP principles + completion plan~~ → **Done** (`v3-completion-plan.md`).
3. **Execute WP1→WP4** (builtins, queries, tests, MCP rewrite).
4. **Freeze V2**, port demos, **delete V2** (WP6–WP8).
5. Expressiveness (Actor, contract gen, …) only when pulled (WP9).

**Related living documents (execution side):**
- `docs/plans/v2-to-v3/master-roadmap.md` — **Authoritative task status**
- `docs/plans/v2-to-v3/orchestration-guide.md` — multi-agent operating model
- `docs/plans/v2-to-v3/agent-summaries/` — executor reports
- `docs/plans/v2-to-v3/workstreams/` — workstream detail
- `docs/plans/v2-to-v3/simple-agent-tasks/` — micro-tasks (**prefer `ws8-*` / `ws4-*` / name-first-consumer; `ws1-*` superseded**)

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