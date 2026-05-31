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
| **Phase 1** | Foundation + Minimal Viable Evolution Layer | High (see workstreams below) | Planning / Early Execution |
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
| **WS1 (merged)** | `workstreams/ws1-evolution-applicator-mvp.md` | Grok (orchestrator) | Claimed – In Progress | — | Working applicator + 6–8 concrete `DomainChange` subtypes + NodeId preservation + MVP operations (PersonLifecycle slice) |
| **WS4** | `workstreams/ws4-trace-and-rollback-ux.md` | TBD | Not Started | WS1 (stable interfaces) | High-quality traces + rollback UX usable by agents + future UI |
| **WS5** | `workstreams/ws5-proof-on-examples.md` | TBD | Not Started | WS1 (first operations) | PersonLifecycle + ≥1 roadblock fully working through the new layer |
| **WS6** | `workstreams/ws6-documentation-hygiene.md` | TBD (light support) | In Progress (orchestrator-led) | All | Decision records, roadmap hygiene, AGENTS.md alignment |
| **WS7 (new)** | `workstreams/ws7-v3-expressiveness-audit.md` | TBD | Not Started | — | Living catalog of V2 concepts vs V3 capability (prevents Phase 4 surprises) |

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

## Next Actions (Current Focus – Under Active Ownership)

1. **WS1 owner (Grok)**: Immediately create the first 8–10 high-quality micro-tasks (starting with concrete `DomainChange` subtypes + applicator skeleton). Personally implement the first 1–2 end-to-end to validate format and unblock smaller agents.
2. Create the consolidated `ws1-evolution-applicator-mvp.md` and `ws7-v3-expressiveness-audit.md`.
3. Create the first 2–3 decision records for Phase 1 choices (Change Representation Strategy, NodeId approach).
4. Exercise the full micro-task → agent-summary → review → plan-update loop at least once.
5. Update remaining workstream files only as needed for interface coordination with the merged WS1.

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
| First batch of concrete micro-tasks ready for small agents | ⚠️ In Progress | WS1 owner creating first 8–10 now (focus on applicator + changes) |
| Clear starting instructions for both orchestrators and executors | ✅ | Updated in this file + ownership plan |
| AGENTS.md + decisions properly reference this plan | ✅ | Strong links exist |
| First Orchestrator has claimed a workstream | ✅ | Grok claimed merged WS1 (Evolution Layer Applicator + MVP Ops) |

**Bottom line**: The *system* is ready. Under current ownership we are closing the "content + ownership" gap immediately rather than waiting for organic progress. The merged WS1 structure reflects ground truth (skeleton exists; the real work is the applicator + concrete operations + NodeId).

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

**What still needs work (being actively closed by WS1 owner):**
- First 8–10 high-quality micro-tasks for the merged WS1 (in progress)
- Consolidated workstream file + WS7 expressiveness audit (in progress)
- First decision records for Change Representation and NodeId strategy
- One full micro-task → summary → review → plan-update cycle exercised

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

Future notes should be added as new dated sections. The original detailed recommendations (merge, fold NodeId, add WS7, update statuses) are now historical and have been executed.

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