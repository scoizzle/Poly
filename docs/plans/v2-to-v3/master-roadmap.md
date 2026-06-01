# V2 → V3 Domain Modeling Port — Master Roadmap

**Status**: Active  
**Purpose**: High-level coordination document for multiple agents working in parallel on the port.  
**Location**: This is the canonical entry point for execution planning.  
**Related**:
- `docs/decisions/2026-v2-to-v3-domain-modeling-port.md` (strategic plan)
- `docs/decisions/2026-core-engineering-principles.md`
- `docs/decisions/2026-05-31-evolution-layer-design.md`

---

## Core Rules for All Agents Working on This Port

1. **Always consult decisions first** (per AGENTS.md).
2. Before claiming or starting any task, check this roadmap and the relevant workstream file for current status and dependencies.
3. When you complete work, create a structured summary in `agent-summaries/` using the template. Only update status in your specific micro-task file if the task instructs you to.
4. Orchestrator agents are responsible for synthesizing summaries into updates to the master roadmap and workstream files.
5. Create or update decision records in `docs/decisions/` for any significant design choices (this is primarily an Orchestrator / WS6 responsibility).
5. Prefer small, verifiable increments. Use the "build working code before abstraction" principle.
6. Multiple agents can (and should) work in parallel on different workstreams when dependencies allow.

---

## High-Level Phases

| Phase | Focus | Parallelism Level | Current Status |
|-------|-------|-------------------|----------------|
| **Phase 1** | Foundation + Minimal Viable Evolution Layer + Quality & Audit | High (see workstreams below) | Foundation Complete (WS1 done); Now focusing on Trace Quality, Expressiveness Audit, and Real Proofs |
| **Phase 2** | Analysis Unification + Lowering Parity | Medium-High | Not Started |
| **Phase 3** | Consumer Migration (MCP + Demos + Tests) — MCP surface actively optimized for model interaction patterns (incremental + fundamental) | High | Not Started |
| **Phase 4** | Full Expressiveness + Roadblock Resolution | Medium | Not Started |
| **Phase 5** | Cutover & Legacy Removal | Low | Not Started |

---

## Phase 1 Workstreams (Designed for Parallel Execution)

These workstreams are structured to allow multiple agents to work concurrently with clear interfaces and minimal blocking.

**Note (2026 ownership update):** WS1 + WS3 + WS2 have been consolidated into a single workstream ("WS1: Evolution Layer Applicator + MVP Operations + NodeId Continuity") per the code review notes in this document and the active ownership plan. WS1 is now the critical path. A new WS7 (V3 Expressiveness Audit) has been added for Phase 1 to make later phases predictable. See the consolidated `workstreams/ws1-evolution-applicator-mvp.md` and `ws7-v3-expressiveness-audit.md`.

| Workstream | File | Owner | Status | Main Dependencies | Primary Deliverable |
|------------|------|-------|--------|-------------------|---------------------|
| **WS1 (merged)** | `workstreams/ws1-evolution-applicator-mvp.md` | Grok (orchestrator) | **Foundation Complete** | — | Working thin evolution layer + MVP operations + NodeId continuity + basic usable traces (see decision 2026-06-phase1-change-representation.md). Phase 1 foundation delivered. |
| **WS7 (new)** | `workstreams/ws7-v3-expressiveness-audit.md` | Grok (orchestrator) | **Complete** | WS1 foundation | Comprehensive living audit complete (full table, roadblock mappings with Suggested Fixes, Phase 4 prioritization). Orchestrator sign-off issued June 2026. |
| **WS4** | `workstreams/ws4-trace-and-rollback-ux.md` | Grok (initial kickoff) | **In Progress** | WS1 + WS7 complete | High-quality, agent-useful traces + rollback UX. Initial improvements started (basic timing added, richer descriptions and diagnostics in progress). |
| **WS5** | `workstreams/ws5-proof-on-examples.md` | Grok | **Complete** | WS1 + WS7 + WS4 | PersonLifecycle + Library Loan Lifecycle both proven end-to-end via evolution layer. Exit criteria met. |
| **WS6** | `workstreams/ws6-documentation-hygiene.md` | TBD (light support) | In Progress (orchestrator-led) | All | Decision records, roadmap hygiene, AGENTS.md alignment |

See the individual files in `workstreams/` for detailed task lists, entry/exit criteria, open questions, and coordination guidance.

---

## Coordination Guidelines for Multi-Agent Work

See the dedicated **[Orchestration Guide](orchestration-guide.md)** for the full operating model. This is the authoritative source for roles, claiming rules, escalation, and how work from all agents (large and small) flows back into the plan via `agent-summaries/`.

**Key principle**: Executor agents (especially smaller models) should almost never directly edit the master roadmap or workstream files. They report via summaries. Orchestrators maintain the shared plan state.

---

## Enabling Smaller / Simpler Agents (Important)

A large portion of the implementation work is being deliberately decomposed into **micro-tasks** suitable for smaller, lower-capability, or cheaper model agents.

See:
- `simple-agent-tasks/README.md` — Guide + principles for creating micro-tasks
- `simple-agent-tasks/` directory — Concrete, narrow tasks that small agents can complete reliably

**Strategy**: Larger/orchestrator agents decompose work into these micro-tasks. Smaller agents execute the majority of the actual code changes.

## Next Actions (Current Focus – Post WS5 Completion)

**Phase 1 is substantially complete. Remaining Phase 1 items are hygiene and polish.**

Phase 1 achievements:
- **WS1 Foundation**: Thin evolution layer on immutable core, NodeId continuity, MVP operations, basic traces ✅
- **WS7 Expressiveness Audit**: Comprehensive V2↔V3 gap catalog, roadblock mappings, Phase 4 prioritization ✅
- **WS5 Proofs**: PersonLifecycle + Library Loan Lifecycle both proven end-to-end (1031 tests) ✅
- **Incremental Analysis Muscle**: `GetAffectedNodes` returns real nodes for all 40+ `DomainChange` subtypes, wired into `Apply(..., priorAnalysis)` ✅
- **DomainChange Coverage Expansion**: 5 new subtypes (SetDomainName, Relationship property management, Property constraint management) + 6 tests ✅

Remaining Phase 1 polish:
1. **WS4 Trace Quality** — Serviceable per WS5 proofs. Remaining: ensure agents get excellent output from the unified diagnostic stream. Lower priority.
2. **WS6 Roadmap Hygiene** — Keep statuses current, orchestrator → agent-summary → plan-update loop. (Active now.)
3. **Decision Record Hygiene** — Keep change representation decision current.

**Phase 2 (Analysis Unification + Lowering Parity) is the next major focus.**
- Unify the V2 and V3 analysis surfaces on the shared Syntax.Analysis infrastructure.
- Achieve lowering parity for the core DomainExpression → C# compilation path (LinqExpressions + CSharpGenerator).
- Phase 2 workstream file now exists at `docs/plans/v2-to-v3/workstreams/ws8-analysis-unification-and-lowering.md`.

**Phase 4 (Full Expressiveness) preparation now active.**
- Dynamic calculation (arithmetic + DateOperation) and read-only RelationshipNavigation for policy rules — documented in `docs/decisions/2026-06-phase4-dynamic-calculation-and-readonly-navigation.md`.
- Cross-entity mutation deliberately excluded: event/subscription pattern preserves ownership boundaries.
- The WS7 audit provides the comprehensive catalog.
- Phase 4 can use the Library domain proof test as its primary acceptance test suite.
- Design doc supersedes `docs/decisions/2026-06-phase4-cross-entity-mutation-and-dynamic-calculation.md`.
- Implementation priority order: 4a (dynamic calc) → 4b (read-only RelationshipNavigation).

---

## Readiness Checklist (Are We Actually Ready to Hand This to Multiple Agents?)

**Current Overall Readiness (under active ownership)**: Ignition in progress — WS1 claimed, first micro-tasks and plan corrections underway. Ready for controlled parallel execution on WS1 support work once the first 5–6 micro-tasks exist.

| Item | Status | Notes |
|------|--------|-------|
| Master roadmap exists and is reasonably clear | ✅ | Updated with merged WS1 + WS7 (2026 ownership) |
| Orchestration Guide exists and is detailed | ✅ | `orchestration-guide.md` |
| Agent summary mechanism + template exists | ✅ | `agent-summaries/` |
| Micro-task template + examples exist | ✅ Good progress | 8+ micro-tasks exist; new batch being added by WS1 owner |
| All Phase 1 workstream files exist | ✅ Now | Consolidated WS1 + new WS7 created under ownership |
| First batch of concrete micro-tasks ready for small agents | ✅ Good progress (WS1 era) | New micro-tasks now being created for post-WS1 priorities (WS7 audit, trace quality, real proofs) |
| Clear starting instructions for both orchestrators and executors | ✅ | Updated in this file + ownership plan |
| AGENTS.md + decisions properly reference this plan | ✅ | Strong links exist |
| First Orchestrator has claimed a workstream | ✅ | Grok claimed merged WS1 (Evolution Layer Applicator + MVP Ops) |

**Bottom line (June 2026)**: Phase 1 foundation complete. WS5 proofs pass (1031 tests). Incremental analysis and DomainChange coverage expansion delivered. Phase 2 (Analysis Unification + Lowering Parity) planning underway. Phase 4 (Full Expressiveness) design work started. Remaining Phase 1: WS4 trace polish + WS6 roadmap hygiene.

**What is solid right now:**
- Clear overall strategy and 5 phases
- Strong multi-agent orchestration model (see Orchestration Guide)
- Good support for smaller agents via micro-tasks + agent-summaries mechanism
- Core decisions documented and linked
- Explicit ownership of the critical path (WS1)

**Important new cross-cutting requirement** (June 2026):
The Evolution layer must support a full real-time visual authoring experience where:
- The UI can render LLM-driven changes live.
- Users can also directly edit the domain through UI controls (adding/editing entities, properties, actions, effects, policies, stages, relationships, etc.).
- Changes from both LLM agents and human users go through the same transactional, analyzable, traceable mechanism.
- The UI can apply changes optimistically with reconciliation against analysis results.

**Critical for implementers**: See the "Anti-Patterns to Avoid in Phase 1 Implementation" section in the Evolution Layer design doc. These are treated as hard constraints on the `DomainChange` model and applicator from the first subtypes.

This requirement is a first-class driver starting in Phase 1.

**What still needs work (Phase 1 → Phase 2 transition):**
- WS4 Trace Quality — Lean model done. Remaining: ensure agents get excellent output.
- WS6 Roadmap Hygiene — Active now. Keep orchestrator → agent-summary → plan-update loop exercised.
- WS8 Phase 2 (Analysis Unification + Lowering Parity) — Workstream file created, execution starts after Phase 1 wrap.
- Phase 4 design — Dynamic calculation + read-only RelationshipNavigation design complete (decision record `2026-06-phase4-dynamic-calculation-and-readonly-navigation.md`, supersedes `2026-06-phase4-cross-entity-mutation-and-dynamic-calculation.md`).
- Decision records — New records needed for Phase 2/4 design decisions.
- Further DomainChange coverage — Remaining gaps (DomainType constraints, Event properties, Relationship shape, Property type changes).

See `00-bootstrap-and-ignition-plan.md` for the ignition sequence. The ownership plan (session plan.md) now drives the first 2–3 weeks of execution.

## Immediate Starting Point (What an Agent Should Do Right Now)

**If you are an Orchestrator / larger agent:**
1. Claim ownership of one workstream in the table above (update this file).
2. Read the corresponding workstream file + the Orchestration Guide.
3. Start decomposing the highest-priority open tasks into micro-tasks in `simple-agent-tasks/`.
4. Create the first 3–5 micro-tasks so smaller agents have something to pick up.

**If you are an Executor / smaller agent:**
- Wait for micro-tasks to appear in `simple-agent-tasks/`.
- Once tasks exist, pick one that matches your capability, follow the template strictly, and submit a summary in `agent-summaries/`.

**First concrete action recommended:**
Create the missing `ws4-trace-and-rollback-ux.md` workstream file + the first batch of micro-tasks from WS1 and WS3.

---

**This document + the workstream files + the Orchestration Guide are the live execution system.** Update them frequently via the proper channels.

---

## Code Review Notes (Added 2026-05-30) — Actioned Under Current Ownership

These notes (based on examining the actual V2 and V3 code) were reviewed during the 2026 ownership planning session. The recommendations below have been implemented (see ownership plan for details).

**Summary of actions taken:**
- WS1 + WS3 + WS2 merged into consolidated `ws1-evolution-applicator-mvp.md`.
- WS7 (V3 Expressiveness Audit) created for Phase 1.
- Master roadmap table, statuses, and "Immediate Starting Point" updated to reflect reality and active ownership.
- WS1 claimed by Grok as orchestrator.
- **WS1 completed** (all exit criteria met, decision record finalized, traces with per-step affected NodeIds, strong PersonLifecycle proof via evolution layer).

Future notes should be added as new dated sections. The original detailed recommendations (merge, fold NodeId, add WS7, update statuses) are now historical and have been executed.

**Phase 1 Re-evaluation (June 2026)**: After WS1 delivered the thin evolution layer foundation, the plan was re-evaluated.

**WS7 Complete (June 2026)**: The V3 Expressiveness Audit is now done (comprehensive table, roadblock mappings with Suggested Fixes, Phase 4 prioritization, orchestrator sign-off). This directly de-risks Phase 4 as intended per the immutable-core decision.

Key shift post-WS1/WS7:
- WS1 = Foundation Complete.
- WS7 = Highest-leverage de-risking item — now Complete.
- Remaining Phase 1: WS4 (Trace Quality for agents), WS5 (Real proofs on documented scenarios + roadblocks with excellent traces), incremental analysis, and hygiene.

This re-prioritization serves the neurosymbolic platform vision and real-time UI requirements. WS4 and WS5 can now proceed with full information.

### 1. Merge WS1 and WS3 — the skeleton exists, the gap is the applicator

In the actual code (`Poly/DomainModeling/Evolution/`):
- `DomainEvolution`, `EvolutionResult`, `EvolutionTrace`, `DomainChange` (abstract base), and `EvolutionBuilder` all already exist and compile.
- The tombstoned `EvolutionTransaction` correctly documents the resolved decision.
- `DomainEvolution.Apply(IReadOnlyList<DomainChange>)` works end-to-end with analysis gating and rollback semantics.

**The only missing piece is**: concrete `DomainChange` subtypes + the applicator that interprets them.

WS1's deliverable ("Core DomainEvolution / Transaction / Trace infrastructure + basic applicator") is 80% done. WS3's deliverable ("First set of useful operations") is the remaining 20%. They are not separable — the applicator IS the operations. Having them as separate workstreams with a dependency arrow between them creates a fake handoff that will slow execution.

**Recommended action**: Merge WS1 and WS3 into a single workstream "Evolution Layer Applicator + MVP Operations." Reassign existing WS1 micro-tasks and WS3 micro-tasks under it. The WS2 workstream can be folded into this merge workstream as a sub-task (NodeId preservation is part of the applicator, not a separate research effort).

### 2. WS2 (NodeId continuity) can be folded into the applicator workstream, not a standalone workstream

NodeId preservation for immutable records is a mechanical `with { Id = node.Id }` copy — see `Poly/Syntax/Node.cs:15` where `NodeId` is a `{ get; init; }` property. This is straightforward to implement inline in the applicator and doesn't warrant a separate workstream with its own owner and dependencies. Fold it into the merged WS1/WS3.

### 3. The workstream table should add a "V3 Expressiveness Gaps" workstream

The plan has no workstream for cataloguing what V3 can't model that V2 can. Based on code analysis, known gaps include:

| Concept | V2 Status | V3 Status |
|---------|-----------|-----------|
| Entity inheritance (`ParentEntity`) | ✅ Entity.cs:47 | ❌ Not present |
| Event subscriptions + correlation | ✅ `EventSubscription.cs` | ❌ Not present |
| Relationship-scoped policies/stages | ✅ `Relationship.cs` has `_policies`, `_stages` | ❌ Relationship.cs has neither |
| Actor entity subtype | ✅ `Actor.cs` | ❌ Not present |
| Rule-composed policies | ✅ `Policy._rules` with 6+ rule subtypes | ❌ Policy uses `DomainExpression` only |

**Recommended action**: Add a WS7 "V3 Expressiveness Audit" workstream for Phase 1. Its deliverable is a catalog document listing every V2 concept and whether V3 can model it, with notes on whether the gap is intentional (simplification) or a missing feature. This prevents Phase 4 from being a reactive scramble when roadblocks surface.

### 4. Update WS1/WS3 dependency and status in the table

WS1's status should reflect reality: "~80% complete (skeleton exists; applicator is the gap)." The current "Not Started" label masks real progress and will confuse agents deciding what to pick up.