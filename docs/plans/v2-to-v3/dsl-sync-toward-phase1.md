# DSL-Engine Sync Plan — Toward Phase 1

**Date:** 2026-07-17
**Revised:** 2026-07-17 (BR.2′–BR.3′′ re-review — suite 1276 green; **commit pending**)
**Status:** Phase 1a vertical **product-complete**; **BR.2′–BR.3′′ implementation complete**, uncommitted
**Current pick:** **Commit BR.2′–BR.3′′** (then BR.4 / E pull-only)
**Source:** [`docs/experiments/domain-modeling-dsl-tour-feedback.md`](../../experiments/domain-modeling-dsl-tour-feedback.md) — §3 and §4  
**Review:** A–D′; N; bundle; BR.3′′; **pre-commit re-review** (2026-07-17)  
**Trigger:** IR/DSL divergence (events vs stage-observation); mutation surface width; single-vertical runtime gap  
**Related:**

| Doc | Role |
|-----|------|
| [`DOMAIN-DSL-SPEC.md`](../../experiments/DOMAIN-DSL-SPEC.md) | DSL vision (design laboratory, not build checklist) |
| [`domain-modeling-dsl-tour-feedback.md`](../../experiments/domain-modeling-dsl-tour-feedback.md) | Direction that this plan executes |
| [`dsl-sync-inventory.md`](dsl-sync-inventory.md) | Slice 0 bill of materials |
| [`vertical-slice-finish-plan.md`](vertical-slice-finish-plan.md) | One open product slice at a time |
| [`mcp-tool-surface-expansion.md`](mcp-tool-surface-expansion.md) | MCP + DSL are **complementary** (do not gut micro-tools) |
| [`../CORE.md`](../../CORE.md) | Platform mechanisms (no reinvention) |
| [`../../AGENTS.md`](../../AGENTS.md) | Principles, placement, build/test |
| [`simple-agent-tasks/vs-README.md`](simple-agent-tasks/vs-README.md) | Break work into pickable micro-tasks before coding |
| ADR [`2026-07-17-stage-transition-as-observable.md`](../../decisions/2026-07-17-stage-transition-as-observable.md) | Stage transition is authorable observable |

**How to use this doc:** Treat §3 slices as the authority for order and exit criteria. Estimates are **rough order-of-magnitude**, not a commitment to a two-week calendar. Prefer promoting concrete steps into `simple-agent-tasks/` when work starts.

**Slice N principle:** N1 changes **surface syntax only**. IR stays (`Relationship` on `Domain`, `AddRelationshipChange`). Parser/printer map nav lines ↔ IR. **Owning/source side is authoritative** — never invent a second edge from reverse-nav lines.

**Status (2026-07-17):** Phase 1a vertical closed (`e3e91ea`). **N+N′** (`9f0707d`) + **bundle** (`a41153c`) shipped. **BR.2′–BR.3′′** in working tree, suite **1276** green. **BR.3′′.2 verified fixed** (throw on Active OnEntry). **Commit is the only open product gate.** Then BR.4 / E pull-only.

---

## 1. What is out of sync

Five concrete gaps between the DSL direction (v0.3) and the current engine IR.

### Gap 1: Event-centric IR vs stage-observation DSL

| DSL (v0.3 settled) | Engine today | Migrate to |
|--------------------|-------------|------------|
| Stage transitions are the observable | First-class `Event` + `PublishEventEffect` + `EventSubscription` + correlation bindings | Stage transition is the only first-class **authorable** observable |
| `when property Stage { effects }` | Event publish/subscribe analyzers | Stage-transition subscription on the subscriber's stage |
| `Entity.Events` references | 4 event-related `DomainChange` types | Remove from product authoring surface |
| Correlation via relationship path | `EventCorrelationBinding` key pairs | Implicit via relationship path — no correlation keys |

**Authorable event IR to remove or replace:**

| Artifact | Action |
|----------|--------|
| `Event` as domain type | **Remove** from product authoring (not “keep as internal Event type” — see §Slice A) |
| `PublishEventEffect` | Remove from product path; transition is the publish |
| `EventSubscription` / routing / correlation | Replace with `StageSubscription` |
| `Entity.Events` / `Entity.EventSubscriptions` | Remove after migration (no long-lived forwarder shim) |
| Event `DomainChange` types (~4) | Remove |
| Event analyzers (4–5) | Remove or rewire to stage-subscription graph |
| Event diagnostic codes (`DMEV*`) | **Retire**; issue **new** codes for stage-subscription (do not renumber/reuse) |

Runtime “entity X entered stage Y” is a **runtime observation**, not an authorable `DomainType`. Do not keep the current `Event` payload type as a fake internal stand-in for that.

### Gap 2: Relationships-as-properties (authoring) vs first-class `Relationship` (IR)

| Target DSL (N1) | Engine after Slice N core |
|-----------------|---------------------------|
| `orders: many owned Order` | **N1 primary** parse + print; N2 still accepted as legacy input |
| Owning/source side only on print | `Relationship` on `Domain` + `SourceOwnsTarget` (IR unchanged) |

**Gap 2 surface closed in core.** Residual honesty: collision policy (N′), MCP smokes for N1 authoring path (N′), commit.

### Gap 3: Phase 1 DSL path — **closed for Phase 1a vertical**

Parser + printer + grammar + MCP `apply_dsl` / `export_dsl` landed. Residual: commit N′′/D′′/BR.1 bundle; **BR.1′** remarks honesty; E pull-only.

### Gap 4: Mutation surface wider than one entity block

~**66** `DomainChange` record types exist. A single DSL entity block maps to many micro-changes or many MCP tool calls. Batch apply of micro-changes (or an optional block DTO) closes the agent cost; gutting discovery tools does not.

### Gap 5: Closed runtime loop for one vertical

| Piece | Status |
|-------|--------|
| `DomainExpression` → Syntax | Complete, tested |
| Effect lowering | Partial (assign/composite/conditional); stage/create/invoke → direct |
| Action execution | `DomainEntityInstance.CallAction` (direct) |
| Stage-scoped `when` | **Thin vertical green** — `DomainInstanceStore` fan-out after CallAction transition (literal effects proven; `event` data-flow residual) |

Remaining runtime fidelity (entry/exit, stage gates, instance links) is **BR residual**, not a blocker for starting DSL Phase 1a.

---

## 2. Principles

1. **Fix the IR first** — do not build a Phase 1 DSL parser against event-centric authoring.
2. **One open product slice at a time** — per `vertical-slice-finish-plan.md`. This plan is a roadmap of slices, not parallel workstreams.
3. **Runtime does not wait on the parser** — builders + evolution already author models. Close the execution loop on API/builders; DSL serializes the same IR.
4. **MCP dual path** — DSL/`apply_dsl` for batch; micro-tools for discovery and repair ([`mcp-tool-surface-expansion.md`](mcp-tool-surface-expansion.md)). Do **not** remove micro-tools as a success criterion.
5. **Keep analyzers healthy while migrating** — do not orphan tests; rewrite or delete with the IR change.
6. **No new IR types without a runtime or analysis consumer** — stage subscription needs analysis + (later) runtime fan-out.
7. **Phase 1 freeze means freeze** — split into **1a (thin)** and **1b (wider)**; reject PRs that pull Phase 2+ constructs into the parser.
8. **One expression IR** — DSL policy/effect expressions parse to existing `DomainExpression` nodes (same semantic model as JSON/MCP paths).
9. **Significant IR choices get an ADR** — event→stage-observation lands a short decision in `docs/decisions/` and updates `docs/CORE.md` in the same change.

---

## 3. Product slices (execution order)

```text
Slice 0–B′     IR + thin runtime                         [done]
Slice C…C′′′   Phase 1a DSL parse/print                  [done]
Slice D + D′   MCP apply/export_dsl + polish             [done]
Slice N + N′   N1 nav surface + residuals                [done — 9f0707d]
Slice N′′      N honesty tests                           [done — a41153c]
D′′.1–.2       MCP honesty nits                          [done — a41153c]
BR.1 / BR.1′   event.* Option B + remarks                [done — a41153c]
BR.2           try/finally flag clear                    [done — a41153c]
BR.2′          event-key cleanup on throw                [impl done — uncommitted]
BR.3           OnEntry/OnExit, stage actions, auto-Add     [impl done — uncommitted]
→              Commit BR.2′–BR.3′′                         [CURRENT]
BR.3′          BR.3 review nits                            [done]
BR.3′′          BR.3′ honesty nits                          [impl done — commit]
BR.4 / E       residual / Phase 1b                         [pull-only / optional]
```

| Slice | Depends on | Does not depend on |
|-------|------------|---------------------|
| 0–D′ | prior | — |
| **N / N′ / N′′** | **D** | **E** / **BR** |
| **BR.1 / BR.1′ / BR.2** | B vertical | — |
| **BR.2′** | BR.2 | — |
| E | named consumer | — |
| BR.3–.5 | B vertical | D |

---

### Slice 0: Survey & inventory

Before IR edits, produce a bill of materials (appendix to this plan or a short tracking note).

- [ ] **0.1** Catalog every `Event`-related type, property, caller, and test.
- [ ] **0.2** Classify each event analyzer: **remove** / **adapt** / **dead**.
- [ ] **0.3** Catalog event-related `DomainChange` types and MCP exposures in `Poly.Mcp/Tools/DomainTools.cs` (not a fictional `V3DomainTools.cs`).
- [ ] **0.4** Catalog relationship authoring paths: builders (`OwnsOne` / `HasMany`), evolution changes, `Property` vs `Relationship` dual form.
- [ ] **0.5** Map `DomainEntityInstance.CallAction` effect dispatch vs `EffectLoweringPass` — ownership baseline for Slice B.
- [ ] **0.6** Count tests that must move or die with event removal.

**Exit:** Written catalog; no product code changes required.

---

### Slice A: Collapse event IR → stage-observation (+ relationships)

Highest leverage. Make stage transitions the only **authorable** observable; delete authorable event surface; define how relationship properties normalize.

#### A.1 Stage-subscription IR

Add a stage-scoped subscription model (effects live on the **subscriber** stage):

```csharp
public enum StageSubscriptionQuantifier {
    Each,   // default — fire per related instance entering a listed stage
    Any,
    All
}

public sealed record StageSubscription(
    string RelationshipName,              // single hop for Phase 1a; no dotted multi-hop yet
    IReadOnlyList<string> StageNames,     // one or more target stages (OR membership)
    StageSubscriptionQuantifier Quantifier,
    IReadOnlyList<Effect> Effects
) : DomainObject;
// Implicit in effect bodies: `this` = subscriber instance; `event` = transitioning instance
// (bind at analysis/runtime — not free-form strings on the record)
```

- [ ] **A.1.1** Add `StageSubscription` + quantifier enum as above.
- [ ] **A.1.2** Add `IReadOnlyList<StageSubscription> Subscriptions` on `Stage` (with `OnEntryEffects` / `OnExitEffects`).
- [ ] **A.1.3** Add query/metadata helper to flatten subscriptions across an entity’s stages (for analyzers).
- [ ] **A.1.4** **No long-lived shim** on `Entity.EventSubscriptions` — migrate callers in this slice; remove the property when migration completes.
- [ ] **A.1.5** Add `AddStageSubscriptionChange` / `RemoveStageSubscriptionChange` (or equivalent evolution API).

**Exit:** Stage subscriptions exist; new tests cover structural rules; green suite after migration steps below.

#### A.2 Remove authorable event surface

- [ ] **A.2.1** Confirm `StageTransitionEffect` is the canonical observable transition (already exists).
- [ ] **A.2.2** Remove or stop authoring `PublishEventEffect` on the product path; rewrite demos (`PersonLifecycleViaBuilders`, etc.) to transition + consumer `when`.
- [ ] **A.2.3** Remove event `DomainChange` types from evolution surface: `AddEventChange`, `RemoveEventChange`, `AddEventReferenceToEntityChange`, `RemoveEventReferenceFromEntityChange`, and event-subscription correlation/routing changes once unused.
- [ ] **A.2.4** Remove `Entity.Events` and `Entity.EventSubscriptions`.
- [ ] **A.2.5** Remove authorable `Event` domain type from product graphs (or leave only if a test proves a second real consumer — default is **remove**).
- [ ] **A.2.6** Update MCP tools in `DomainTools.cs` that expose event authoring; replace with stage-subscription tools if needed. Honesty: do not claim runtime subscription firing until Slice B.

#### A.3 Analyzers

| Analyzer | Action |
|----------|--------|
| `EventFlowAnalyzer` | **Remove** |
| `CorrelationAnalyzer` | **Remove** (correlation is relationship path) |
| `EventContractAnalyzer` | **Adapt** → `SubscriptionContractAnalyzer` (path resolves; stages exist on target; effects bind `this`/`event`) |
| `CausalityAnalyzer` | **Adapt** — action → stage transition → subscription effect graph |
| `ReplaySafetyAnalyzer` | **Adapt** — subscription-triggered effects |
| Structural / effect / constraints / … | **Keep** |

- [ ] **A.3.1** Remove flow + correlation passes and their tests.
- [ ] **A.3.2** Adapt contract / causality / replay; register in `DomainModelAnalysisBuilderExtensions`.
- [ ] **A.3.3** Retire old `DMEV*` codes; allocate **new** codes for stage-subscription diagnostics (never reuse numbers).

#### A.4 Relationship-as-property normalization

Define and implement one of these (prefer N1 unless inventory shows N2 is faster for Slice B):

| Option | Behavior |
|--------|----------|
| **N1 (preferred)** | Entity-typed property line authoring creates/updates a domain `Relationship` + property projection; cardinality from `many` vs singular; `owned` → `SourceOwnsTarget`; reverse nav synthesized for analysis |
| **N2 (temporary)** | Dual form documented: builders/MCP still use `AddRelationshipChange`; DSL later maps property lines → same changes; printer emits property-line form |

- [x] **A.4.1** **N2 interim accepted for B** — first-class `Relationship` only; property-line DSL deferred to Slice C (see A′′.5 / appendix).
- [x] **A.4.2** Vertical need met via `AddRelationshipChange` + subscription by relationship name (N1 not required for B).
- [ ] **A.4.3** Structural analyzer: reject illegal shapes (unknown target type, `owned` without clear owner, duplicate edge). *(Still open — pull when N1 or Slice C needs it.)*

#### A.5 Decision record + CORE

- [x] **A.5.1** ADR: `docs/decisions/2026-07-17-stage-transition-as-observable.md`
- [x] **A.5.2** `docs/CORE.md` DomainModeling row notes stage transitions as authorable observable.

#### A tests

- [x] **At.1** Rewrite/remove tests that create events via `DomainChange` or builders.
- [x] **At.2** New tests for stage-subscription structural + contract diagnostics. → **Slice A′ / `SubscriptionAnalysisTests.cs`**
- [x] **At.3** DomainModeling tests green with subscription coverage.

**Slice A land:** Event product surface removed; stage-observation IR in place; ADR + CORE updated.

---

### Slice A′: Fix-up after Slice A code review — **DONE (core)**

**Implemented (verified 2026-07-17 code review):**

| Item | Status | Evidence |
|------|--------|----------|
| **A′.1.1–1.2** Semantic remove | Done | `RemoveStageSubscriptionChange.SemanticMatch` (RelationshipName + StageNames + Quantifier); test `RemoveStageSubscription_ByReconstructedKey_RemovesSubscription` |
| **A′.1.3** Zero-match fail-loud | **Open → A′′.1** | Remove does not call `RequireTarget` on zero matches; test `WhenNoMatch_ReportsError` actually asserts **success** (misnamed) |
| **A′.2** Contract analyzer | Done | Resolves relationship (source entity + name), target entity, target stages; `DMSS003`; honest docs + TODO for `this`/`event` |
| **A′.3** StageBuilder + tests | Done | `Subscribe(...)` wires `Subscriptions`; builder + evolution happy-path tests |
| **A′.4.1** Causality docs + coarse test | Done (heuristic) | Analyzer remarks document mutual-subscription heuristic; `CausalityAnalyzer_MutualSubscription_ReportsCycle`. Precise transition graph → post-B |
| **A′.4.2** Replay link/unlink | Done (impl) | `LinkRelationshipEffect` / `UnlinkRelationshipEffect` included; **no dedicated test** → optional A′′ |
| **A′.5.1** EventCount | Done | Removed from `DomainOverview` + `DomainOverviewData` / MCP |
| **A′.5.2** Property wording | Done | Event dropped from `Property` remarks |
| **A′.5.3** N2 interim note | **Open → A′′.5** | Still first-class `Relationship` only; document in plan appendix |

**A′ core exit:** Green for **direct-API Slice B** (subscription IR authorable, analyzable, removable by key). Remaining items are **A′′**.

---

### Slice A′′: Residual fix-ups after A′ implementation review — **DONE**

**Verified 2026-07-17 (A′′ impl review):** build green; DomainModeling + MCP smoke green.

| Item | Status | Evidence |
|------|--------|----------|
| **A′′.1** Fail-loud zero-match remove | Done | `RequireTarget` when no `SemanticMatch`; test `RemoveStageSubscription_WhenNoMatch_FailsLoud`; remove-all documented on change type |
| **A′′.2** Query + MCP subscriptions | Done | `SubscriptionDetail` on `StageDetail`; MCP `SubscriptionData` / `StageData.subscriptions`; test `Query_StageSubscription_AppearsInEntityDetail` |
| **A′′.3** Duplicate key warning | Done (R2) | Contract analyzer warns on duplicate semantic keys; `Analyze_DuplicateSubscriptionKeys_ReportsWarning` |
| **A′′.4** Quantifier vs OneToOne | Done (partial) | Warns `Any`/`All` on `OneToOne`; test `Analyze_AnyQuantifierOnOneToOne_ReportsWarning`. **ManyToOne** not yet treated as singular → **B-prep.1** |
| **A′′.5** N2 interim | Done | Plan appendix |
| **A′′.6** Polish | Open / deferred | See **B-prep** below |

**A′′ exit:** Met for product progression to Slice B.

---

### B-prep / residual follow-ups (from A′′ code review) — do **not** block Slice B start

These are small honesty/quality items. Prefer folding **B-prep.2–.3** into Slice B as you touch runtime; leave pure nits for a polish pass.

#### B-prep.1 — Quantifier vs **ManyToOne** (and any singular source) (optional)

**Gap:** `SubscriptionContractAnalyzer` only flags non-`Each` when `Cardinality == OneToOne`. From the **subscriber/source** side, `ManyToOne` is also singular (one target), so `Any`/`All` are equally meaningless.

**Do:** Treat “singular from source” as `OneToOne | ManyToOne` (or equivalently: not `OneToMany | ManyToMany`). Keep as Warning + `DMSS003` (no new code unless you prefer `DMSS004`).

- [ ] **B-prep.1.1** Extend quantifier check + test for `ManyToOne`

#### B-prep.2 — MCP smoke for subscription visibility (optional honesty)

**Gap:** Query unit test exists; no MCP test asserts `get_entity_detail` JSON includes `subscriptions` after evolve/add.

**Do:** One `McpSmokeTests` path: create session → entity → stages → relationship → (if MCP lacks add_subscription tool, build domain offline or use evolution API in test harness) → `GetEntityDetail` → assert `StageData.Subscriptions` non-empty.  
If MCP has **no** `add_stage_subscription` tool yet, either add a thin MCP wrapper over `AddStageSubscriptionChange` (still dual-path micro-tool) **or** skip smoke until D/B MCP expansion — document honesty: agents cannot *author* subscriptions via MCP tools today, only *see* them if present.

- [ ] **B-prep.2.1** MCP smoke **or** note “author via evolution/builders only”
- [ ] **B-prep.2.2** (optional) MCP `add_stage_subscription` micro-tool for agent authoring

#### B-prep.3 — Slice B runtime (primary — not optional)

Carry these as first-class B tasks (already in B.1–B.4); restated from review so they are not lost:

| Need | Why |
|------|-----|
| Fan-out on stage transition | `DomainEntityInstance.TransitionStage` does not notify subscribers today |
| `this` / `event` binding | Subscription effects need subject resolution (analyzer TODO already) |
| Instance store / domain graph | Finding “who points at me via relationship X” requires multi-instance context beyond single `CallAction` |
| Depth limit + cycle safety | Pair with coarse `SubscriptionCausalityAnalyzer` |

- [ ] Implemented under **Slice B** checklist below (B.1–B.4)

#### B-prep.4 — Small polish nits (optional)

- [ ] **B-prep.4.1** Replay hint text still says “create or transition” while code also flags link/unlink — fix message; optional `DMSS002` test with `CreateEntityInstance` in subscription effects
- [ ] **B-prep.4.2** `DomainQueries.cs`: remove duplicate/orphan `/// <summary>` above `SubscriptionDetail` (doc nit only)
- [ ] **B-prep.4.3** DRY: `SemanticMatch` / `SemanticKeyMatch` duplicated in `DomainChange` and `SubscriptionContractAnalyzer` — extract shared static helper only if a third call site appears
- [ ] **B-prep.4.4** Causality full path + transition-aware graph → **post-B** (analyzer already documents heuristic)
- [ ] **B-prep.4.5** `StageBuilder.Subscribe` multi-stage list overload → Phase 1b / Slice E

---

### Slice B + B′: Thin runtime loop — **DONE (vertical)** / residual polish open

**Goal (met for thin vertical):**

```text
CallAction → StageTransitionEffect → DomainInstanceStore.NotifyTransition
  → stage-scoped StageSubscription effects (Each, source=subscriber, type-level)
```

**Verified 2026-07-17 (B′ impl review):**

| Item | Status | Evidence |
|------|--------|----------|
| **B′.1** CallAction notifies | Done | `TransitionStage(..., notifyStore: true)` from effects; `_isExecutingSubscription` suppresses re-notify; `CallAction_StageTransitionEffect_FiresSubscriptionOnRelatedInstance` |
| **B′.1** Wrong-stage non-fire | Done | `CallAction_StageChange_SubscriptionDoesNotFireWhenSubscriberInWrongStage` |
| **B′.3** `store.Add` owns Store | Done | `Add` sets `instance.Store = this`; tests only call `Add` |
| **B′.4** Source = subscriber | Done | Store matches Target=transitioned + Source=subscriber; tests use `Tracker ──Tracks──► Order`; analyze clean |
| **B′.5** Type-level doc | Done | Remarks on `DomainInstanceStore` |
| **B′.2** `event` data-flow | **Partial** | Snapshot copy fixed; keys merged as `"event.{prop}"` into `_values` during subscription effects — **no test** that RHS reads event props; convention is string-prefix not real DE `event` node |

**B vertical exit:** Treat as **green for literal subscription effects** via CallAction. Do **not** claim full DSL `when` body expressiveness until residual B′.2 test lands.

---

### B residual (optional — from B′ code review; does **not** block Slice C)

#### BR.1 — Prove `event.*` property flow — **DONE** (`a41153c`, Option B)

- [x] **BR.1.1** Characterization test: `SubscriptionEffect_EventPropertyAccess_Gap_Documented`
- [x] **BR.1.2** Option B documented on `ExecuteSubscriptionEffects` + `EventPrefix` XML
- [x] **BR.1.3** Test asserts fire (`!= "UNTOUCHED"`) **and** gap (`!= "ABC-123"`)
- [ ] **BR.1.4** (Future, named consumer) Real `event` subject / lowering — do not invent opcodes now

#### BR.2 / BR.2′ — Exception safety — **IMPL DONE** (uncommitted)

- [x] **BR.2.1** `try/finally` clears flag + event instance on throw (`a41153c`)
- [x] **BR.2′.1** `eventKeys` outside `try`; remove in `finally`
- [x] **BR.2′.2** Stale “standard lowering path” comment removed
- [x] **BR.2′.3** Exception smoke test (throw propagates; Status unchanged)
- [x] **BR.2′.4** Snapshot assert: no `"event."` keys after throw; second transition still runs notify (flag cleared)
- [ ] **BR.2′.5** **Commit** (with BR.3)

**Review (2026-07-17, re-check):** Production + strengthened test look good.

#### BR.3 — Lifecycle completeness — **IMPL DONE** (uncommitted)

Order implemented: **OnExit → set stage → OnEntry → NotifyTransition** (when `notifyStore` and not inside subscription).

- [x] **BR.3.1** OnExit before leave; OnEntry after enter; notify after entry — tests `OnExitEffect_RunsBeforeStageTransition`, `OnEntryEffect_RunsOnStageTransition`
- [x] **BR.3.2** Stage-local actions preferred over entity-level; missing when not on that stage — `StageScopedAction_OnlyCallableFromThatStage`
- [x] **BR.3.3** `Store?.Add(child)` after create — `CreateEntityInstance_AutoAddsToStore` (asserts `child.Store != null`)
- [x] **BR.3.4** One-hop cascade (sub transitions subscriber) — `Subscription_OneHop_Cascade` (depth-limit enforcement tested in `Subscription_Cascade_ExceedsDepthLimit`)

##### BR.3′ — follow-ups from BR.3 review — **IMPL LANDED** (uncommitted)

- [x] **BR.3′.1** Hierarchy walk — code + BR.3′′.1 test
- [x] **BR.3′.2** OnEntry try/finally notify — code + BR.3′′.2 test (fixed: throws on Active OnEntry)
- [x] **BR.3′.3** Depth limit — E11 subscribed (BR.3′′.3)
- [x] **BR.3′.4** Child store fan-out — good
- [x] **BR.3′.5** Re-entrancy documented (BR.3′′.4)
- [ ] **BR.3′.6** **Commit** (with BR.3′′.7)

##### BR.3′′ — honesty nits — **IMPL DONE** (uncommitted)

- [x] **BR.3′′.1** `StageAction_InheritedFromParentStage` — parent-chain inheritance — **verified**
- [x] **BR.3′′.2** `OnEntryEffect_Throws_StageStillSet_NotifyStillFires` — **re-reviewed fixed**: throw is on **Active OnEntry**; asserts `threw`, stage Active, B notified — exercises notify-in-finally
- [x] **BR.3′′.3** E11 real subscription — maxDepth proof — **verified**
- [x] **BR.3′′.4** Re-entrancy remarks on `TransitionStage` — **verified**
- [x] **BR.3′′.5** Policy vs action hierarchy asymmetry remarks — **verified**
- [x] **BR.3′′.6** `Subscription_OneHop_Cascade` rename — **verified**
- [ ] **BR.3′′.7** **Commit** BR.2′ + BR.3 + BR.3′ + BR.3′′ (prod + tests + plan) — **NOT done** (working tree dirty; plan previously overstated “commit done”)

**Pre-commit re-review (2026-07-17):** No remaining production defects found in this bundle. Suite **1276** green. Residual after commit: **BR.4** / **E** only (pull-only / optional).

#### BR.4 — Earlier B-prep leftovers (still optional)

- [ ] **BR.4.1** Quantifier `Any`/`All` on `ManyToOne` (not only `OneToOne`)
- [ ] **BR.4.2** MCP smoke for subscriptions / optional `add_stage_subscription` tool
- [ ] **BR.4.3** Replay message includes link/unlink; duplicate XML on `ExecuteSubscriptionEffects`; DRY `SemanticMatch`
- [ ] **BR.4.4** Instance-level relationship links (second consumer)

#### BR.5 — Doc / style nits

- [x] **BR.5.1** Already resolved by BR.1′ `<remarks>` insertion — method and `EventPrefix` each have one `<summary>` (no duplication)
- [x] **BR.5.2** Trailing newline restored on `DomainEntityInstanceTests.cs` (confirmed via xxd)

---

### Slice B exit criteria (updated)

- [x] CallAction + StageTransitionEffect fires matching subscription (literal effect)
- [x] Wrong-stage subscriber does not fire
- [x] store.Add wires Store; Source=subscriber; type-level documented
- [x] Test domains analyze without DMSS003
- [x] Event property flow: Option B characterized + remarks honest (`a41153c`)
- [x] DomainModeling tests green
- [x] BR.2 / BR.2′ + BR.3 + BR.3′ + BR.3′′ (working tree; **commit CURRENT**)

**B vertical product path is closed.** Residual BR work is optional polish / depth.

---

### Slice C + C′ + C′′ + C′′′: Phase 1a parse/print — **DONE**

**Verified 2026-07-17 (C′′′ review):** build green; **17** Parsing tests green.

| Item | Status | Evidence |
|------|--------|----------|
| C′.2–C′.5 | Done | is not, pattern, primitives once, relationships, subscription |
| C′′.1 deferred require | Done | order-independent; missing throws; no Literal(true) |
| C′′.2 no `when_*` policies | Done | consume only |
| C′′.3 grammar sync | Done | relationship + require docs |
| C′′.4 unsupported keywords | Done (throw) | actor/value/create + malformed |
| C′′.5 printer honesty | Done | omit entry/exit; `require not` print |
| **C′′′.1** entity-level `require not` | **Done** | Guard removed; always `Add`; `C3_RequireNot_EntityLevel_BindsRealExpression` |

**Frozen N2 relationship syntax:**

```text
relationship <Name> from <SourceEntity> to <TargetEntity> one|many
```

#### Optional residual (post-D polish / BR)

- [ ] CallAction e2e: DSL-parsed `require` blocks instance action when policy false
- [ ] `ParseResult` multi-error (D wraps `FormatException` today)
- [ ] Document one polarity per `require` line
- [ ] owned relationships / richer fixtures
- [ ] **BR.*** event data-flow, stage-gated CallAction, entry/exit

---

### Slice D + D′: MCP apply/export_dsl — **DONE** (verified)

**Commit:** `e3e91ea`. MCP smoke DSL tests green (ApplyDsl_*/ExportDsl_*).

| Item | Status | Evidence |
|------|--------|----------|
| **D.1** apply_dsl | Done | parse → evolve empty domain → strict analysis → `Replace` |
| **D.2** replace semantics | Done | tool + README |
| **D.3** micro-tools retained | Done | still registered |
| **D.4** export_dsl | Done | round-trip smoke |
| **D.5** no Capture | Done | not implemented |
| **D′.1** early session check | Done | `TryGet` before parse |
| **D′.2** honesty in tool description | Done | HONESTY NOTES: stage `when`, instance store, revision+1 |
| **D′.3** revision monotonic | Done | `current.Revision + 1` (not 0) |
| **D′.4.1** export → apply_dsl affordance | Done | |
| **D′.4.2** empty polyText | Done | |
| **D′.4.4** require CallAction e2e | Done | `ApplyDsl_WithRequire_BlocksCallActionWhenPolicyFails` |
| Program | Done | `.WithTools<DslTool>()` |

**Shipped apply semantics:** empty `Domain` + parser primitives → replace session domain; revision **+1**.

---

### Slice N: N1 relationship-as-navigation-property authoring — **DONE** (`9f0707d`)

**What:** Prefer N1 **source-side** nav lines inside entities over N2 top-level `relationship … from … to …` lines. IR unchanged (`Relationship` / `AddRelationshipChange`). Parser + printer (+ EntityDetail) map surface ↔ IR.

**Why now:** Phase 1a vertical shipped. N2 is IR-shaped authoring; modelers/agents expect `orders: many Order` (see `DOMAIN-DSL-SPEC.md` Relationship Syntax).

**Scope:** Parser + Printer + grammar doc + tests + EntityDetail navigations. **No IR changes. No analyzer changes. No new DomainChange types.**

**Out of scope for N:** instance-level links (BR.4); reverse FK columns; ManyToMany both-sides inventiveness; Phase 1b (`create in`, etc.).

**Implementation review (2026-07-17):** Core N correct. **N′** residuals landed and re-reviewed (below). Full product exit for N = **commit**.

---

#### Critical design rules (from Slice N design review — **do not violate**)

1. **Source side is authoritative.** A nav line on entity `S` means `AddRelationshipChange(Name, Source=S, Target=T, …)`. Do **not** emit a second `AddRelationshipChange` when the same edge is mentioned on the target entity.
2. **Printer default: source side only.** Do **not** print reverse-nav lines that re-use the same relationship name — re-parse would create a second edge or clash. Optional later: reverse as documentation-only alias with a distinct name and alias detection (DSL-spec “optional reverse name”) — not Phase N.1.
3. **Syntax aligns with DOMAIN-DSL-SPEC (Phase 1a subset):**

```text
nav-line = identifier ":" [ "many" ] [ "owned" ] entity-type-name
// many → OneToMany; omit many → OneToOne (singular)
// owned → SourceOwnsTarget = true
// optional: "one" as explicit singular alias of bare type (if easy; not required)
```

Examples:

```text
orders: many owned Order
manager: Employee
supplier: owned Supplier
```

4. **Defer relationship changes until all entities are known** (or until end of parse). Do **not** require “target entity must appear above source in the file” — that fights alphabetical export and natural authoring. Collect pending navs; resolve after entity set is complete; error if target type is not an entity (and not a primitive).
5. **N2 coexistence:** Keep parsing top-level `relationship Name from Src to Tgt one|many` for a transition window. **Printer emits N1 only** (source-side nav lines). Contradictory earlier note “remove top-level loop” vs “keep for transition” → **keep parse, drop print**.
6. **Tokenizer reality:** `one` / `many` are already `TokenKind.One` / `TokenKind.Many` (from N2). Route them after `:` in entity body; do not claim they are bare Identifiers today.
7. **Round-trip metric:** structural domain equality (entities, relationship count/edges, cardinality, ownership) after `parse → evolve → print → parse → evolve` — **not** byte-identical text. Entity order may stay alphabetical.
8. **Subscriptions:** still use relationship **name** (`when Tracks Active`). Nav line `tracks: many Order` → relationship name `tracks` (or normalize casing consistently — pick **preserve identifier as Name**).

---

#### N.0 — Spec freeze before code

- [x] **N.0.1** Freeze nav grammar in `dsl-phase1a-grammar.md` (N1 primary §2.9; N2 demoted to legacy §2.8)
- [x] **N.0.2** Document source-only print rule + deferred resolution + ownership
- [x] **N.0.3** Fixtures via `N1NavigationTests` (N2-only, N1-only, N2-inside-entity). Mixed N1+N2 in one file → N′.5 if desired

#### N.1 — Printer: source-side nav properties

```text
Customer: entity {
  Name: Text
  orders: many Order          // from Relationship where Source == Customer
}
// Order entity does NOT get "orders: one Customer" auto-printed
```

- [x] **N.1.1** Printer holds `domain.Relationships`; emits navs inside `PrintEntity`
- [x] **N.1.2** Source-side emit: `many` / bare / `owned`; name = `rel.Name`
- [x] **N.1.3** No reverse-nav print
- [x] **N.1.4** Top-level N2 **output** loop removed
- [x] **N.1.5** Tests: `N2Input_PrintsAsN1_RoundTrips`, `N1Nav_RoundTrips_StructurallyIdentical`

#### N.2 — Parser: nav lines inside entity blocks

Pattern after `name :` in entity body:

| Tokens after `:` | Meaning |
|------------------|---------|
| `many` [`owned`] `Type` | OneToMany (+ owned?) |
| `owned` `Type` | OneToOne + owned |
| `one` [`owned`] `Type` | OneToOne (+ owned?) optional alias |
| primitive type | existing property path |
| bare `Type` (non-primitive, not keyword) | OneToOne nav to entity `Type` |

- [x] **N.2.1** `IsNavLine` + `ParseNavLine` before property path
- [x] **N.2.2** `PendingNav` queue; target need not appear earlier in file
- [x] **N.2.3** `ResolvePendingNavs` after all entities; unknown / primitive → `FormatException`. Self-cycle left allowed (Friends-style) — confirm with N′.6 if product wants a ban
- [x] **N.2.4** Top-level + in-entity N2 parse retained
- [x] **N.2.5** Core syntax tests in `N1NavigationTests`
- [x] **N.2.6** Collision: same name as property on entity → parse/analyze error → **N′.1**
- [x] **N.2.7** Two navs / edges with same relationship name (domain-unique today via structural analyzer only after evolve) → clearer parse error → **N′.2**

#### N.3 — EntityDetail navigations

- [x] **N.3.1** `NavigationDetail(RelationshipName, RelatedEntityName, Role, Cardinality, SourceOwnsTarget)`
- [x] **N.3.2** `EntityDetail.Navigations`
- [x] **N.3.3** Populated from `domain.Relationships` (source **and** target **views** — query only)
- [x] **N.3.4** MCP `NavigationData` / `EntityDetailData.navigations`
- [x] **N.3.5** Dedicated tests for source and target views → **N′.3**

#### N.4 — Docs

- [x] **N.4.1** Grammar doc N1 primary, N2 legacy input
- [x] **N.4.2** Plan appendix: N2 interim → “legacy accepted input / N1 canonical print”
- [x] **N.4.3** Printer class doc; MCP apply_dsl description mentions N1 nav lines
- [x] **N.4.4** Gap 2 updated (this revision). Formal grammar §1 includes optional trailing N2 production → **N′.4**

#### N.5 — Compat + regression

- [x] **N.5.1** Full suite green with N2 fixtures still parsing (1255 tests)
- [x] **N.5.2** Subscription round-trip green via N2→N1 print path (`C5_*`, `N1NavWithSubscription_RoundTrips`); product N1 path covered by **N′.8** / apply_dsl. True unit-level N1-authored C5 → **N′′.1** (optional honesty)
- [x] **N.5.3** apply_dsl smoke: N1 multi-entity file with nav + subscription → **N′.8**
- [x] **N.5.4** Export of session built via micro-tool `add_relationship` prints N1 (source side) → **N′.9**

**Slice N core exit (met):**

- [x] Printer emits N1 source-side nav lines only (no N2 output)
- [x] Parser accepts N1 nav lines → single IR edge per nav; deferred resolution
- [x] N2 input still accepted
- [x] Structural round-trip green; subscription-by-name still works
- [x] N.3 navigations on EntityDetail + MCP mirror
- [x] Grammar doc matches (formal §1 includes optional N2 production)

**Slice N full exit** = core + N′ + **commit** (N′′ optional).

**Risks (explicit):**

| Risk | Mitigation |
|------|------------|
| Reverse print same name → double edge on re-parse | Source-only print (rule 2) — verified in printer |
| Forward-ref entity targets | Deferred emit (rule 4) — verified |
| `many`/`one` as property names | They are keywords already; rare — document |
| Bare `Foo: Bar` vs typo for primitive | Error if `Bar` not entity and not primitive |
| MCP micro-tools still N2-shaped | Fine — export normalizes to N1 (N′.9 asserts) |
| Property name vs nav name collision | Parse-time via `_entityPropertyNames` (N′.1) |
| Duplicate relationship names | Parse-time via `_relationshipNames` for N1 + N2 (N′.2) |

---

### Slice N′: N residuals — **DONE** (committed in `9f0707d`)

- [x] **N′.1**–**N′.12** as previously checked (collisions, EntityDetail, grammar, mixed/self-ref, MCP smokes, error msg, nits)
- [x] Shipped with N core in commit `9f0707d`

#### N′ implementation review (2026-07-17) — verified at land

| Check | Result |
|-------|--------|
| Design rules 1–2–4–5 | Pass |
| Collisions N′.1–.2 | Pass + tests |
| MCP N′.8 / N′.9 | Pass; honesty nits closed in N′′ |
| Commit | **`9f0707d`** |

---

### Slice N′′: honesty test polish — **DONE** (`a41153c`)

- [x] **N′′.1**–**N′′.7** as previously checked
- [x] **N′′.8** Committed in `a41153c`

#### Post-commit review of `a41153c` (2026-07-17)

| Check | Result |
|-------|--------|
| N′′.1–.4 tests | Pass |
| D′′.1–.2 | Pass |
| BR.1 / BR.1′ | Pass — Option B remarks honest; dual assert |
| BR.2 try/finally (a41153c) | Flag cleared; event-key leak fixed by BR.2′ |
| BR.2′ (working tree) | Keys in `finally`; smoke green; Snapshot assert optional |
| Suite | **1267** green with BR.2′ |

---

### Post–N residuals

#### D′′ — Tiny MCP nits — **DONE** (`a41153c`)

- [x] **D′′.1** README Tool Honesty DSL paragraph
- [x] **D′′.2** `apply_dsl` re-batch affordance
- [x] **D′′.3** Session race (accepted)

#### BR residual (runtime depth)

- [x] **BR.1** + **BR.1′** Option B (`a41153c`)
- [x] **BR.2** + **BR.2′** (`finally` + Snapshot; uncommitted)
- [x] **BR.3** + **BR.3′** lifecycle/hierarchy/cascade/auto-Add (uncommitted)
- [x] **BR.3′′** honesty nits complete in tree; **commit open** (BR.3′′.7)
- [ ] **BR.4** quantifiers / MCP sub tools / instance links
- [x] **BR.5** style nits closed earlier

#### Slice E: Phase 1b grammar (**pull-only**)

Only with a **named consumer** for value types / `create in` / quantifiers / etc.

- [ ] Freeze Phase 1b grammar delta
- [ ] Parser + printer + tests
- [ ] Runtime for any new effects on green path

**Do not** start E to “fill the void” after N.

---

## 4. What does NOT happen in this plan

| Thing | Why not | When |
|-------|---------|------|
| Remove MCP micro-tools | Breaks discovery; contradicts MCP expansion plan | Never as a success criterion |
| Capture mode on first `apply_dsl` | Honesty/trust feature | Separate workstream |
| Actor keyword / entity extension | Not needed for thin vertical | After slices A–D as needed |
| `parallel` / `schedule` / `for` / match / collection query DSL | Second system | Spec later |
| Full effect catalog | Pull-only | Named consumer |
| Codegen (REST, gRPC, HATEOAS, schema) | After closed loop | Post B+ |
| Library import / domain kinds | Multi-domain | Later |
| JSON wire → DSL only | JSON stays for machines | Never — complementary |
| Keeping authorable `Event` “for lowering” | Wrong type for runtime observation | Do not |
| Renumbering old diagnostic codes | Breaks agents/tests | Retire + new codes only |

---

## 5. Risk assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Analyzer gap during event collapse | Medium | Medium | Adapt causality/replay/contract; document removed codes |
| Parser scope creep | High | High | Phase 1a freeze doc; reject 1b/2+ in PRs; NOT YET SUPPORTED |
| Round-trip instability | Medium | Medium | Structural equality gate in CI |
| Second execution framework (`Orchestrator` vs `CallAction`) | High | High | Single ownership rule in Slice B |
| Subscription cycles | Medium | Medium | CausalityAnalyzer + runtime depth limit |
| Relationship dual form forever | Medium | Medium | Slice N: N1 print + N2 input window; then demote N2 |
| Reverse-nav double edge on re-parse | High if reverse printed | High | Source-only print (Slice N design rule 2) |
| Forward-ref entity in nav line | Medium | Medium | Defer AddRelationshipChange until entities known |
| MCP honesty (tools claim runtime too early) | Medium | High | Slice D descriptions; no execute claim without tool |
| Calendar optimism | High | Medium | Slice exits over day counts; promote agent micro-tasks |

---

## 6. Effort (order of magnitude, not a sprint commitment)

| Slice | Rough effort | Notes |
|-------|--------------|--------|
| 0 Inventory | 0.5–1 day | Done |
| A IR delete + StageSubscription shell | — | Done |
| A′ Fix-up (match, contract, builder, tests) | 0.5–2 days | Done |
| A′′ Residual honesty/query | 0.5–1 day | Done |
| B + B′ Runtime vertical | — | **Done** (CallAction→when literals) |
| B residual (event test, entry/exit, …) | 0.5–2 days | Optional polish |
| C … C′′′ Phase 1a parse/print | — | Done (~17 Parsing tests) |
| **D + D′ MCP apply/export** | — | **Done** (commit `e3e91ea`) |
| **N N1 nav surface (core)** | **1–2 days** | Done (`9f0707d`) |
| **N′ residuals** | **0.5–1 day** | Done (`9f0707d`) |
| **N′′ polish** | **&lt;0.5 day** | Done (`a41153c`) |
| D′′ tiny nits | &lt;0.5 day | Done (`a41153c`) |
| E Phase 1b | Pull-only | Named consumer only |
| BR residual | weeks | Optional depth; BR.2′ small |

**Dependency summary:**

```text
0 → … → B+B′ → C…C′′′ → D+D′  ✅ Phase 1a vertical closed
                         ↘ N+N′ ✅ `9f0707d` → N′′/D′′/BR.1/BR.1′/BR.2 ✅ `a41153c`
                         ↘ BR.2′–BR.3′′ ✅ ready (commit CURRENT)
                         ↘ BR.4 / E pull-only
```

---

## 7. Exit criteria (program done when all applicable slices green)

### Slice A (+ A′ + A′′)

- [x] No authorable `Event`, `EventSubscription`, `EventCorrelationBinding`, `EventSubscriptionRoutingMode`, or `PublishEventEffect` on the product path.
- [x] Stage subscriptions on `Stage`; event analyzers removed; `DMSS*` passes registered.
- [x] ADR + CORE updated.
- [x] **A′ core:** semantic remove; real contract analyzer; builder subscribe; subscription tests.
- [x] **N2 interim** for relationships (first-class records); N1 deferred to Slice C (now delivered in Slice N).
- [x] **A′′:** fail-loud zero-match remove; query/MCP subscription visibility; duplicate-key warning; OneToOne quantifier check.

### Slice B + B′

- [x] **CallAction** → effects → stage → `when` (Each) → subscriber side effects (literal assign).
- [x] Single execution ownership; `DomainInstanceStore` not a second product API.
- [x] B′.1 / B′.3 / B′.4 / B′.5 done.
- [x] B′.2 / BR.1 / BR.1′ Option B characterized + documented (`a41153c`)
- [x] BR.2 try/finally (`a41153c`) + **BR.2′** + **BR.3** + **BR.3′** + **BR.3′′** (**commit CURRENT**)
- [ ] BR.4 optional residuals

### Slice C … C′′′ — **DONE**

- [x] Phase 1a grammar + parser/printer + **17** Parsing tests.
- [x] N2 relationships; deferred require; no when_* stubs; entity-level `require not` fixed.

### Slice D + D′ + D′′ — **DONE**

- [x] Strict `apply_dsl` + `export_dsl`; replace + revision+1; early session check; empty text fail.
- [x] Honesty notes; dual-path README; require CallAction e2e; D′′.1–.2 (`a41153c`)
- [x] Micro-tools retained.

### Slice N + N′ + N′′ — **DONE**

- [x] N1 surface + residuals (`9f0707d`) + honesty tests (`a41153c`)

### Cross-cutting

- [x] Suite green (**1276**); BR.3′′.2 fixed and verified.
- [x] D+D′ (`e3e91ea`); N+N′ (`9f0707d`); N′′/D′′/BR.1–2 (`a41153c`); BR.2′–BR.3′′ uncommitted.
- [x] `AGENTS.md` principles unchanged unless a principle itself changes (rare).

---

## 8. Appendix — revision log

| Date | Change |
|------|--------|
| 2026-07-16 | Initial plan from tour feedback |
| 2026-07-17 | Review patch: five gaps; slices 0/A–E; runtime before/parallel to DSL; MCP dual path; relationship normalization; StageSubscription shape; CallAction ownership; Phase 1a/1b; retire-not-renumber diagnostics; remove Event-as-internal; fix DomainTools path; drop Packaging; Capture out of first apply_dsl; realistic effort |
| 2026-07-17 | **Slice A′** added after code review: fix `RemoveStageSubscription` Node.Id equality bug; implement real `SubscriptionContractAnalyzer`; subscription tests + `StageBuilder` subscribe; optional causality/replay/EventCount nits. Slice B blocked on A′. |
| 2026-07-17 | A′ **implementation reviewed**: core A′ done. **A′′** added: fail-loud remove, query/MCP visibility, duplicate-key, quantifier, N2. |
| 2026-07-17 | A′′ **implementation reviewed**: A′′.1–.5 done. Current pick was Slice B. |
| 2026-07-17 | B **scaffold reviewed**: B′ tasks added (CallAction notify, event bag, store.Add, Source=subscriber). |
| 2026-07-17 | B′ **implementation reviewed**: CallAction→when vertical green. Recommended next was Slice C. |
| 2026-07-17 | C **scaffold reviewed**: C′ tasks added. |
| 2026-07-17 | C′ **implementation reviewed** → C′′ tasks. |
| 2026-07-17 | C′′ review → C′′′.1 `require not` StageName bug. |
| 2026-07-17 | **C′′′ verified.** Next was D. |
| 2026-07-17 | **D + D′ shipped** (`e3e91ea`). Phase 1a vertical closed. |
| 2026-07-17 | **Slice N added** then **design-reviewed**: surface-only N1; **source-side authoritative**; no same-name reverse print; deferred relationship emit; syntax `many`/`owned`/bare; N2 input kept, N1 print only; `one`/`many` already tokens. |
| 2026-07-17 | **Slice N implementation reviewed**: core correct; **N′** opened for collisions/MCP smokes. |
| 2026-07-17 | **N′ landed** then **N′ impl review**: collisions + MCP smokes verified; suite **1263** green. **N′′** opened for honesty nits. |
| 2026-07-17 | **N+N′ committed** (`9f0707d`). |
| 2026-07-17 | **Bundle committed** (`a41153c`): N′′ + D′′.1–.2 + BR.1/BR.1′/BR.2. Suite **1266**. |
| 2026-07-17 | **Post-commit review of `a41153c`**: residual BR.2′ opened (event-key finally). |
| 2026-07-17 | **BR.2′ landed** then **strengthened** (Snapshot + second notify); suite **1267** then **1272** with BR.3. |
| 2026-07-17 | **BR.3 landed** (uncommitted): OnExit→set→OnEntry→notify; stage-scoped CallAction; auto Store.Add; one-hop cascade. |
| 2026-07-17 | **BR.2′+BR.3 impl review**: residual BR.3′ opened. |
| 2026-07-17 | **BR.3′ landed** (uncommitted): parent-stage action walk; OnEntry try/finally notify; depth cascade test; child store fan-out. Suite **1274**. |
| 2026-07-17 | **BR.3′ impl review**: production mostly sound. Gaps: **no hierarchy unit test**; depth test **E11 has no subscription** (weak); OnEntry re-entrancy **not** store-depth-limited (claim overstated). Residual **BR.3′′**. **Commit CURRENT**. |
| 2026-07-17 | **BR.3′′ landed** (uncommitted): hierarchy test; E11 sub; docs; OneHop rename; OnEntry-throw test. Suite **1276**. |
| 2026-07-17 | **BR.3′′ impl review**: **BR.3′′.2 false positive** — throw was on **Draft** OnEntry, not **Active**. |
| 2026-07-17 | **BR.3′′.2 fixed** then **pre-commit re-review**: throw on Active OnEntry; `threw` asserted; notify-in-finally proven. Bundle **ready to commit** (BR.3′′.7 open). No further product follow-ups beyond BR.4/E pull-only. |

### Appendix — Relationship authoring

| Mode | Form | Role after Slice N |
|------|------|---------------------|
| **N1 (canonical print + preferred author)** | `orders: many owned Order` on **source** entity | Printer output; primary parse path |
| **N2 (legacy input)** | `relationship Orders from Customer to Order many` | Still accepted during transition |
| **IR** | `Relationship` on `Domain` | Unchanged |

Runtime correlation remains **type-level** until BR.4.4.

### Appendix — Agent pick order (after pre-commit re-review)

| Order | Task | Severity | Blocks |
|-------|------|----------|--------|
| 1 | **Commit** BR.2′ + BR.3 + BR.3′ + BR.3′′ (prod + tests + plan) | Required | Uncommitted work |
| 2 | **BR.4 / E** | Optional / pull-only | Named consumer |

**Implementer watch-outs:**

- Do not mark BR.3′′.7 done until `git` shows a commit hash.
- Prefer commit now; BR.4 only with a named consumer.


Principles: minimal diffs; TUnit names `Method_Condition_ExpectedResult`; no new abstractions; do not reintroduce Event/Publish. **Never attach always-true policies as stand-ins for missing requires or stage gates.**
