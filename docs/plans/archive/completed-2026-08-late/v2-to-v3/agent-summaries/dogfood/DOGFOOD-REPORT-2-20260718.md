# MCP Dogfood Report 2 — 2026-07-18 (Post-RT)

## Executive recommendation
**Next product slice:** **Builder API honesty & effect micro-tools** — fix the `AddActionToStage`/effect-adder mismatch and add proper stage-scoped effect editing

**Why (top finding):** With Runtime MCP shipped, the #1 remaining pain is API **surprise**: `AddActionToStage` creates empty action copies whose effects can't be modified through the builder. Combined with entity-level policies gating all actions, agents building models incrementally hit silent failures (actions that resolve but do nothing, or policies that block unexpectedly).

**Not next:** DMAS001 hint discoverability (Score 15). Important but cheaper — it's a single-line filter change vs. a new API surface.

## Ranked pains (Top 8)

| Rank | ID | Score | Cat | Title | Backlog bucket |
|------|-----|-------|-----|-------|----------------|
| 1 | C4-F2 | 15 | A | DMAS001 hints invisible through `GetAnalysisSummary` | other (suggestion visibility) |
| 2 | C4-F3 | 14 | W | No guard against CallAction on deleted instances | other (runtime safety) |
| 3 | — | 14 | W | `AddActionToStage` creates empty action copies — effects don't apply | effect-micro |
| 4 | — | 12 | H | Entity-level policies gate ALL actions on entity (non-obvious) | guide-honesty |
| 5 | — | 10 | O | Subscription directionality hard to reason about (target vs source) | V1/S1 |
| 6 | — | 8 | T | No `AddEffectToActionOnStage` — can't modify stage-scoped action effects | effect-micro |
| 7 | — | 6 | E | CallAction doesn't report which policy failed in some scenarios | V1/S1 |
| 8 | — | 5 | D | Export DSL missing subscription effects in some edge cases | guide-honesty |

## What RT fixed (since first dogfood)

| Gap (first report) | Runtime MCP fix | Status |
|--------------------|----------------|--------|
| **R** — No CallAction/instance tools | `create_instance` + `get_instance` + `list_instances` + `call_action` | ✅ Fully working |
| **R** — No instance management | Session-scoped `DomainInstanceStore` + `InstanceMap` | ✅ Working |
| **R** — Stage subscriptions model-only | `NotifyTransition` fan-out via store (verified: C1 golden path fires 2 subscriptions) | ✅ Working |
| **R** — No spawn-and-wire in MCP | End-to-end: DSL → create → call_action → subscription effects → observe | ✅ Verified |

### What worked (keep)

| Area | Detail |
|------|--------|
| **Batch DSL → RT pipeline** (C1) | 3-entity domain with 2-stage lifecycle, guards, 2 subscriptions, create-in → all work end-to-end. Customer transitions through Order lifecycle correctly. |
| **Round-trip idempotence** | Both batch DSL and micro-built domains export/re-parse cleanly. Micro-built Clinic (379 chars) round-trips identically. |
| **Guard policy evaluation** | `PositiveTotal` policy correctly blocks negative-total orders. Entity-level policies evaluated. |
| **Snapshot fidelity** | `DomainEntityInstance.Snapshot()` correctly returns all property values after lifecycle transitions. |
| **Subscription cascades** | Verified that linked subscriber instances receive subscription effects when related entity transitions. |

## Finding details

### 1. DMAS001 hints invisible (Score 15)
**Evidence:** Entity with 2 properties (Text + Number with range) → 0 stages → `AuthoringSuggestionAnalyzer` generates 2 Hint diagnostics. `GetAnalysisSummary` returns them as `InfoCount=3` but **zero** Messages (only Error+Warning pass through). Agent calling `get_domain_analysis` gets no hint of suggestions.

**Workaround exists:** Call `get_domain_suggestions` separately (it correctly filters Hint + DMAS001). **Cost:** Agent must know about the second tool.

### 2. No guard on deleted instances (Score 14)
**Evidence:** `DomainEntityInstance.CallAction` does not check `IsDeleted`. After `DeleteEntityInstance` effect sets `IsDeleted=true`, calling any action still succeeds. Instance must be removed from `DomainInstanceStore` to be unreachable via `get_instance`, but the instance object itself is still callable.

**Workaround:** Caller must check `IsDeleted` before `call_action`. `Store.Remove()` prevents lookup but doesn't prevent direct API calls.

### 3. AddActionToStage creates empty copies (Score 14) — NEW
**Evidence:** `AddActionToStage(entity, stage, name)` applies `AddActionToStageChange` which creates a **new standalone action**:
```csharp
Actions = s.Actions.Append(new Action(Name, InvocationResult.Void, [], [], [])).ToList()
```
This new action has **no effects, no policies**. The effect-adder methods (`AddStageTransitionEffect`, `AddEffectToAction`, `AddActionWithEffect`) all target the **entity-level** action. When `CallAction` resolves from the current stage, it finds the stage-scoped copy (which does nothing) and never reaches the entity-level action with effects.

**Repro:**
1. `AddAction("Order", "Activate")` — entity-level action
2. `AddActionToStage("Order", "Draft", "Activate")` — stage-scoped copy (empty effects)
3. `AddStageTransitionEffect("Order", "Activate", "Active")` — adds effect to ENTITY-level copy only
4. Instance on "Draft" stage calls `CallAction("Activate")` → resolves stage-scoped copy → succeeds but does nothing (no effects)

**Workaround:** 
- Use `AddActionWithEffect` (creates entity-level action with effects) and DON'T use `AddActionToStage`
- Or construct actions manually and add to both entity and stage (as `CallAction_WithPassingGuards_Succeeds` test does)
- Entity-level actions still resolve when the stage doesn't have a same-named action (CallAction fallback)

### 4. Entity-level policies gate all actions (Score 12)
**Evidence:** When a policy is added at entity level (via `AddPolicyToEntity`), it's evaluated as a **universal guard** for ALL actions via `CallAction`. An agent expecting entity-level policies to be "just descriptions" or "optional filters" will find actions unexpectedly blocked.

**Example from C3:** `HighPriority` policy on `Ticket` entity (Priority >= 5 AND Completed == false) — blocks `Start`, `Resolve`, and `Reopen` when Priority < 5, even though none of those actions reference `require HighPriority`.

**This IS correct behavior** (entity-level policies act as always-on guards), but it's **non-obvious** from tool descriptions. The `add_policy` tool description says "Adds a policy with a guard expression to an entity" — doesn't explain it gates all actions.

### 5. Subscription directionality (Score 10)
**Evidence:** Stage subscriptions fire when the relationship **TARGET** enters a stage — not the source. An agent creating `AddRelationship("r", "A", "B")` and then adding a subscription on A's stage for relationship "r" expecting to be notified when A transitions will be confused. The store looks for relationships where the transitioned entity is the **target**.

### 6. No `AddEffectToActionOnStage` (Score 8)
**Evidence:** There is no builder method to add effects to a stage-scoped action. All effect-adder methods (`AddEffectToAction`, `AddStageTransitionEffect`, `AddActionWithEffect`) target entity-level actions. Stage-scoped actions can only get effects at construction time (manual `new Action(name, result, params, effects, policies)`).

### 7. CallAction error reporting (Score 6)
**Evidence:** `CallAction` returns `ActionCallResult` with `FailedGuards` list, but when an action is not found (stage-scoped empty copy issue), the error message says "not found" without guidance about the entity vs stage ambiguity.

## Coverage matrix

| Mission | Completed | Pain findings | Notes |
|---------|-----------|---------------|-------|
| C1: Batch DSL + RT | ✅ | 0 (verified) | Full spawn-and-wire works end-to-end |
| C2: Micro + RT | ✅ | 0 | Incremental building fine; C2 behavioral test |
| C3: Oracle + deep RT | ✅ | 0 (tool results) | Entity-level policy blocking identified |
| C4: Adversarial | ✅ | 2 | DMAS001 invisibility, deleted-instance guard |
| Exploratory | ⚠️ | 4 identified | Builder API mismatch, policy honesty, subscription direction, no stage-effect API |

## Recommended next-slice rationale (per §9.1)

```text
No R finding remains (RT fixed the #1 pain).
Top is A/W — suggestion invisibility (cheap fix) + builder API surprise (medium slice)
```

**IF top is H/D with clear small fix → honesty/guide/parser fix slice**
**ELSE IF top is A and text hints useless → structured suggestions**
**ELSE IF top is T and agents cannot edit effects without full apply_dsl → effect micro-tools**

Top finding is Score 15 (A — hint invisibility) but the **highest-impact** pain is the builder API mismatch (Score 14, affects every agent building incrementally). The fix requires:
1. Clear documentation on `AddActionToStage` behavior
2. Either: `AddEffectToActionOnStage` API for modifying stage-scoped effects
3. Or: have `AddActionToStage` create a **reference** to the entity-level action instead of an empty copy

**Recommendation:** **Effect micro-tools + builder API honesty** — add `add_effect_to_action` and/or `add_effect_to_stage_action` MCP tools (wrapping the builder), and fix the `AddActionToStage` documentation/behavior.

## Evidence links

| File | Path |
|------|------|
| C1-DSL-RT | `C1-dsl-rt-20260718.md` |
| C2 Micro RT | `C2-micro-rt-20260718.md` |
| C3 Oracle RT | `C3-oracle-rt-20260718.md` |
| C4 Adversarial | `C4-adversarial-rt-20260718.md` |
| Findings JSON | `dogfood2-findings-20260718.json` |
| Prior report | `DOGFOOD-REPORT-20260718.md` |
