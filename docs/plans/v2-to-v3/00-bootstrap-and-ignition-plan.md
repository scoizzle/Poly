# V2 → V3 Port — Bootstrap & Ignition Plan (First 1–2 Weeks)

> **Historical note (June 2026):** This document describes the initial ignition sequence and is retained for provenance.
>
> For current status and active priorities, use:
> - `docs/plans/v2-to-v3/master-roadmap.md`
> - `docs/plans/v2-to-v3/workstreams/`

**Purpose**: This is the practical "ignition sequence" to move from planning into real parallel implementation with multiple agents (including smaller ones).

It assumes the overall plan, workstreams, orchestration model, and documentation guardrails are already in place.

## Historical State Assessment (at Draft Time)

**Strengths**:
- Excellent high-level plan and supporting structure (`master-roadmap.md`, workstreams, orchestration guide, agent-summaries mechanism, micro-task templates).
- Strong documentation discipline and decision linkage.
- Good support for mixed agent sizes.
- UI/real-time authoring requirements are now explicitly called out as a first-class driver (with anti-patterns documented).

**Main Gaps Preventing Safe Parallel Execution**:
- No one has claimed ownership of any workstream yet (this remains the #1 blocker).
- A growing but still insufficient number of concrete micro-tasks exist (we added several more; need more for WS1 especially).
- There is no defined "Week 0 / First Sprint" with named first tasks and sequencing.
- The critical path (WS1) still needs further decomposition into the first 5–8 micro-tasks.

## Historical Ignition Sequence

### Step 0: Designate Initial Orchestrator(s) (Blocking)

**Action**: Explicitly assign 1–2 people/agents as the initial Orchestrator(s) for Phase 1.

**Why it's blocking**: Without clear ownership, decomposition into micro-tasks won't happen, and smaller agents will have nothing to work on.

**Suggested first claim** (update the master roadmap):
- Claim **WS1 (Evolution Layer Core Infrastructure)** as the first workstream.

### Step 1: Create the First Wave of Micro-Tasks (Highest Leverage)

The Orchestrator should immediately decompose the highest-priority items from WS1 and WS3 into 8–12 concrete micro-tasks.

**Priority order for first micro-tasks**:

**From WS1 (Critical Path)**:
1. Define `EvolutionResult` record (simple, clean data carrier).
2. Define `EvolutionTrace` + `EvolutionStep` records (focus on agent-useful fields).
3. Implement minimal `DomainEvolution` (Apply batch + `Evolve()` fluent builder) with no-op applicator (post the decision to drop the explicit transaction model — see 2026-05-31-evolution-layer-design.md).
4. Implement a basic "no-op" or identity change handler + test.
5. Add basic successful evolution + analysis-error-returns-rolled-back-result tests.
6. Sketch/propose the first cut of a fluent Evolution API surface (see `spikes/fluent-evolution-api-proposal.md`). The fluent builder is now the main ergonomic investment on the simpler core.

**From WS3 (to give smaller agents work quickly)**:
7. Implement `AddProperty` operation (change + applicator logic + trace + test).
8. Implement `AddStage` operation (similar).
9. Implement basic `AddAction` (without effects first).

**Cross-cutting**:
10. Create the decision record for the MVP change representation strategy (orchestrator-led).

These first 8–10 micro-tasks should be created **before** trying to run many agents in parallel.

### Step 2: Establish the First Feedback Loop

- Have at least one Executor agent (even if it's the same person wearing two hats initially) pick up one of the first micro-tasks.
- Require them to submit an agent summary.
- Have the Orchestrator review it and update the master plan.
- This validates the agent-summaries + orchestration loop before scaling to more agents.

### Step 3: Lock the MVP Scope for the First 4–6 Weeks

Before opening the floodgates to many agents, the Orchestrator(s) + you should agree on:

- Exact MVP scope of operations for the first milestone (see suggestions in the Phase 1 breakdown).
- The initial change representation approach (native `DomainChange` + minimal adapter?).
- Whether the fluent evolution surface is in scope for the first milestone or the one after.

### Step 4: Light Documentation Hygiene Pass (Parallel)

While the above is happening, the WS6 (Hygiene) agent (or the Orchestrator) should do a quick pass to ensure:

- All new micro-tasks properly reference AGENTS.md + relevant decisions.
- The "Anti-Patterns to Avoid" from the Evolution Layer design doc are referenced in the relevant workstreams.

## Success Criteria for "Ignition Complete"

- At least one workstream (ideally WS1) has a clear owner.
- 10–15 high-quality micro-tasks exist and are ready for smaller agents (we made good progress in this session).
- The agent-summaries + review loop has been exercised at least once with a real task.
- A clear "Week 0" sequence of the first 5–7 concrete tasks is defined and visible.
- Any blocking design decisions for the first 2–3 weeks have been made (or explicitly deferred).

## Recommendation

Do **not** try to get "everything perfect" before starting.

The highest value right now is to:

1. Designate the first Orchestrator.
2. Create the first 8–10 micro-tasks (especially the WS1 skeleton ones).
3. Run one small end-to-end cycle (micro-task → summary → integration into plan).

Once that loop is working, you can rapidly scale the number of agents.

This is the classic "get the system working with one or two agents first, then parallelize."

---

**Status of this document**: Created as a focused ignition plan to bridge from the excellent high-level planning to actual parallel implementation.

---

## Code Review Notes (Added 2026-05-30)

### 1. Step 1 micro-task priority should lead with DomainChange subtypes, not the fluent API

The ignition plan's Step 1 lists task 6 ("Sketch/propose the first cut of a fluent Evolution API surface") alongside defining the core EvolutionResult/EvolutionTrace records. The fluent API should come after — not alongside — the DomainChange subtypes. The first 8-10 micro-tasks should include "Define 5-8 concrete DomainChange record types" and "Implement the applicator that transforms DomainChange list → new Domain."

### 2. The "Fluent Evolution API Surface" micro-tasks should not start until the change model is stable

Sketching a fluent API before the DomainChange types exist means the API design will be speculative and need rework. The correct sequence: DomainChange records → applicator → fluent EvolutionBuilder surface → then evaluate ergonomics against builders.