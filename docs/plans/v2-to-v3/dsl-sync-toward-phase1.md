# DSL-Engine Sync Plan — Toward Phase 1

**Date:** 2026-07-16  
**Revised:** 2026-07-17 (Slice C DSL scaffold reviewed; C′ fix-ups are current pick)  
**Status:** Active roadmap — execute as **product slices** (see §3), not as one uninterrupted sprint  
**Current pick:** **§3 Slice C′** (parser/printer correctness — blocks treating C as done)  
**Source:** [`docs/experiments/domain-modeling-dsl-tour-feedback.md`](../../experiments/domain-modeling-dsl-tour-feedback.md) — §3 and §4  
**Review:** Plan review; Slice A–B′ and Slice C scaffold code reviews (2026-07-17)  
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

**Status (2026-07-17, post C scaffold review):** Runtime vertical green (B+B′). **Slice C scaffold untracked:** `PolyDslTokenizer` / `PolyDslParser` / `DomainDslPrinter` + `dsl-phase1a-grammar.md` + 3 round-trip tests. Scaffold is **not** C-exit quality — see **C′** (require/when semantics wrong; pattern bug; no relationships; no diagnostics surface; weak tests).

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

| DSL | Engine today |
|-----|--------------|
| `orders: many owned Order` | `Property(Name, Type, Constraints)` — no cardinality/ownership |
| Owning side only; reverse nav synthesized | `Relationship` on `Domain` with `Cardinality`, `SourceOwnsTarget` |

Without a normalization rule, Phase 1 parse/print cannot be correct. This is IR work, not only a parser mapping note.

### Gap 3: No Phase 1 DSL path

No parser, importer, exporter, or committed `.poly` format. MCP is incremental only; no batch text apply.

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
Slice 0–A′′  IR + analyzers + query/MCP honesty                    [done]
    │
Slice B+B′   Thin runtime: CallAction → stage → when (Each)         [done for vertical]
    │
Slice B residual  event data-flow test, entry/exit, stage gates     [optional polish]
    │
Slice C      Phase 1a grammar + parser/printer scaffold                [partial]
    │
Slice C′     Fix parse semantics, relationships, diagnostics, tests    [CURRENT pick]
    │
Slice D      MCP apply_dsl (strict) + dual path
    │
Slice E      Phase 1b grammar (pull-only)
```

| Slice | Depends on | Does not depend on |
|-------|------------|---------------------|
| 0–B′ | prior | — |
| C scaffold | A′ IR | — |
| **C′** | **C scaffold** | MCP |
| D | C′ exit | — |
| E | C′ + named consumer | — |

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

#### BR.1 — Prove `event.*` property flow (recommended before DSL `when` bodies)

**Gap:** `ExecuteSubscriptionEffects` injects `"event." + prop` keys into `_values`. There is **zero** test that an `AssignEffect` RHS uses `DomainExpression.Property("event.SomeProp")` (or better DE shape) and copies data from the transitioned instance.

**Risk:** Dictionary-backed VM member access may not treat `"event.X"` like a real property; type defs omit those keys. Vertical today only proves **literal** assigns.

**Do:**

1. Add test: Order has property `Code`; subscription on Tracker assigns `Status` from event code (whatever expression shape works).
2. If string-prefix fails, implement thin support (e.g. parameter map / dual subject) **or** document Option B: subscription vertical = literals/`this` only until DSL parser defines `event`.
3. Prefer not inventing domain opcodes.

- [ ] **BR.1.1** Failing-or-passing test for event→subscriber assign
- [ ] **BR.1.2** Fix runtime or document Option B in `DomainEntityInstance` remarks

#### BR.2 — Exception safety for `_isExecutingSubscription` (nit / bug)

**Gap:** Flag set true without `try/finally`. If `ExecuteEffect` throws, subsequent action transitions on that instance never notify.

**Do:** wrap subscription effect loop in `try/finally` clearing flag + event keys.

- [ ] **BR.2.1** `try/finally` cleanup

#### BR.3 — Lifecycle completeness (post-vertical)

- [ ] **BR.3.1** Run `OnExit` / `OnEntry` effects on stage transition (order: exit → set stage → notify? or notify after entry — pick one, document, test)
- [ ] **BR.3.2** Stage-gate actions: `CallAction` only succeeds if action is offered by current stage (or entity-level)
- [ ] **BR.3.3** `CreateEntityInstance` children: auto `Store.Add` when parent has store
- [ ] **BR.3.4** Cascade test: subscription body transitions subscriber → depth-limited second notify

#### BR.4 — Earlier B-prep leftovers (still optional)

- [ ] **BR.4.1** Quantifier `Any`/`All` on `ManyToOne` (not only `OneToOne`)
- [ ] **BR.4.2** MCP smoke for subscriptions / optional `add_stage_subscription` tool
- [ ] **BR.4.3** Replay message includes link/unlink; duplicate XML on `ExecuteSubscriptionEffects`; DRY `SemanticMatch`
- [ ] **BR.4.4** Instance-level relationship links (second consumer)

#### BR.5 — Doc nit

- [ ] **BR.5.1** Remove duplicate `/// <summary>` blocks on `ExecuteSubscriptionEffects` in `DomainEntityInstance.cs`

---

### Slice B exit criteria (updated)

- [x] CallAction + StageTransitionEffect fires matching subscription (literal effect)
- [x] Wrong-stage subscriber does not fire
- [x] store.Add wires Store; Source=subscriber; type-level documented
- [x] Test domains analyze without DMSS003
- [ ] Event property flow tested **or** Option B documented (BR.1)
- [x] DomainModeling tests green

**Recommended next product slice:** **Slice C** (Phase 1a DSL). Pull BR.1 if C will emit `event` in `when` bodies immediately.

---

### Slice C: Phase 1a grammar freeze → parse → evolve → print — **PARTIAL**

**Files (untracked at review):**

| File | Role |
|------|------|
| [`dsl-phase1a-grammar.md`](dsl-phase1a-grammar.md) | Frozen grammar (authoritative vs experiment DSL spec) |
| `Poly/DomainModeling/Parsing/PolyDslTokenizer.cs` | Hand-written scanner |
| `Poly/DomainModeling/Parsing/PolyDslParser.cs` | RD parser → `DomainChange[]` |
| `Poly/DomainModeling/Parsing/DomainDslPrinter.cs` | Domain → `.poly` |
| `Poly.Tests/DomainModeling/Parsing/PolyDslRoundTripTests.cs` | 3 minimal tests (pass) |

#### C.1–C.4 status

| Item | Status |
|------|--------|
| **C.1.1** Grammar doc | Done (needs consistency fixes — see C′.6) |
| **C.2.1** Parser + tokenizer | Scaffold done; **semantics incomplete** (C′) |
| **C.2.2** Expressions → `DomainExpression` | Partial (`is not` wrong — C′.2) |
| **C.2.3** NOT YET SUPPORTED diagnostics | **Missing** — throws `FormatException` only |
| **C.2.4** SupplyChain-class fixture | **Missing** |
| **C.3** Printer | Scaffold; no relationships; prints unparseable entry/exit blobs |
| **C.3.2** Structural round-trip | Weak (name/counts only; one entity) |
| **C.4** Tests | 3 happy-path only |

**Do not claim Slice C exit until C′ required items are green.**

---

### Slice C′: Fix Phase 1a parser/printer (CURRENT) — from C scaffold review

**Audience:** Implementing agent.  
**Source:** Code review of untracked Parsing/* + grammar + tests (2026-07-17).

#### C′.1 — Bug: `require` / `when` gates are fake policies (required)

**Symptom:** In `ParseActionBody`:

```csharp
// require HasName → Policy("HasName", Literal(true))  // always true, not entity policy
// when Draft → Policy("when_Draft", Literal(true))    // not a stage gate
```

Runtime `CallAction` evaluates `action.Policies` as boolean guards. So:

- `require HasName` does **not** evaluate the entity policy `HasName` expression.
- `when Draft` does **not** restrict availability to stage Draft (and BR.3.2 stage gates are still missing at runtime anyway).

**Do (pick one coherent model for Phase 1a):**

| Option | Behavior |
|--------|----------|
| **G1 (recommended)** | `require PolicyName` → attach **reference** to existing entity `Policy` by name (copy expression from entity policy at parse time, or store name-only if IR supports it). Fail parse/analyze if policy missing. |
| **G2** | Drop `require`/`when` from Phase 1a parser until stage-gated `CallAction` exists; document exclusion. |

For **stage `when` gates:** until runtime stage-gates exist, either omit from Phase 1a or store as metadata (not `Literal(true)` policies). Do **not** invent always-true policy stubs.

**Tests:** parse `require HasName` with real entity policy body; after evolve + analyze, action guard evaluates false when subject fails policy (or analysis proves binding).

- [ ] **C′.1.1** Fix require → real policy binding
- [ ] **C′.1.2** Fix or defer stage `when` gates honestly
- [ ] **C′.1.3** Tests

---

#### C′.2 — Bug: `is not` parses as `Equal(left, Not(right))` (required)

**Symptom:** `ParseComparison` treats `TokenKind.Is` as comparison, then `ParsePrimary` on `not null` becomes `Not(null)`. Result: `Name is not null` → `Equal(Name, Not(null))` instead of `NotEqual(Name, null)`.

Dead code path later looks for `Is` + peek `Not` after comparison already consumed `Is`.

**Do:** On `Is`, peek `Not`; if present, consume both and emit `NotEqual`. Else emit `Equal`.

**Test:** `Parse_IsNotNull_ProducesNotEqual`.

- [ ] **C′.2.1** Fix `is not` / `is` parse
- [ ] **C′.2.2** Test

---

#### C′.3 — Bug: `pattern("...")` captures wrong token (required)

**Symptom:**

```csharp
Expect(TokenKind.StringLiteral); // advances past string
var pattern = _current.Text;     // next token (often `)`) — wrong
```

**Do:** capture return value of `Expect(TokenKind.StringLiteral)` (or read text before second Expect).

**Test:** `Parse_PatternConstraint_StoresRegex`.

- [ ] **C′.3.1** Fix pattern capture
- [ ] **C′.3.2** Test

---

#### C′.4 — `EnsurePrimitives` duplicates on multi-entity files (required)

**Symptom:** Every `ParseEntity` emits 5× `AddPrimitiveTypeChange`. `AddPrimitiveTypeChange.ApplyTo` always `Types.Add` — second entity → duplicate primitive names → structural failure / pollution.

**Do:** Emit primitives **once** per parse (after domain header), or skip if name already queued in this parse’s change list. Prefer bootstrap via `DomainFactory.Create(name)` then only structural changes (cleaner).

**Test:** two-entity `.poly` evolves successfully; primitive count = 5 (or factory set), not 10.

- [ ] **C′.4.1** Single primitive bootstrap
- [ ] **C′.4.2** Multi-entity fixture test

---

#### C′.5 — Relationships (N2) missing from parse/print (required for subscriptions)

**Gap:** Grammar/output table lists `AddRelationshipChange`; parser never emits relationships; printer never prints them. Stage subscriptions cannot analyze clean without edges.

**Do (Phase 1a N2 form — pick one syntax and freeze in grammar doc):**

```text
// Recommended explicit form (matches IR, not property-line N1):
relationship Tracks from Tracker to Order one
// or block form — whatever is frozen in dsl-phase1a-grammar.md
```

1. Update `dsl-phase1a-grammar.md` with the chosen N2 relationship syntax.
2. Parser → `AddRelationshipChange` (cardinality + optional owned).
3. Printer → emit same form from `domain.Relationships`.
4. Test: two entities + relationship + subscription parses, evolves, analyzes without `DMSS003`.

- [ ] **C′.5.1** Grammar N2 relationship syntax
- [ ] **C′.5.2** Parse + print
- [ ] **C′.5.3** Subscription end-to-end DSL fixture

---

#### C′.6 — Grammar doc consistency (required hygiene)

| Issue | Fix |
|-------|-----|
| §7 EBNF wraps domain in `{ }` | Match §1 / implementation (no domain braces) |
| Example places `HasName: policy` **outside** entity | Policies are entity members only in parser |
| Output list mentions parameters / entry/exit | Phase 1a parser has no action params, no entry/exit syntax — remove or mark out of scope |
| Named range args claimed | Parser does not implement `range(min: 0)` — implement or drop claim |

- [ ] **C′.6.1** Align grammar doc with parser reality

---

#### C′.7 — Diagnostics / unsupported constructs (required for agent honesty)

**Gap:** Plan requires parse errors as diagnostics, not throws; unsupported keywords should be “not yet supported”. Today: `FormatException` only.

**Do (minimal):**

1. `ParseResult { Changes, Diagnostics }` or collect `List<string>` / `Diagnostic` without throwing on first error where practical.
2. On keywords `actor`, `value`, `create`, `schedule`, `for`, `parallel`, `import` — emit stable not-yet-supported message (may still abort further parse of that construct).
3. Tests: unsupported construct yields message containing “not supported in Phase 1a” (or grammar table wording).

- [ ] **C′.7.1** Result type or diagnostic list
- [ ] **C′.7.2** At least 3 unsupported-keyword tests

---

#### C′.8 — Printer honesty (required)

| Issue | Fix |
|-------|-----|
| Prints OnEntry/OnExit as bare effects inside stage | Unparseable; omit until grammar has `entry`/`exit`, or print comment `// on-entry` |
| Prints `if` for `ConditionalEffect` | Not in Phase 1a grammar — omit or comment |
| No relationship output | C′.5 |
| Fake `when_*` policies round-trip as `require when_Draft` | Goes away when C′.1 fixed |

- [ ] **C′.8.1** Printer only emits Phase 1a-parseable surface
- [ ] **C′.8.2** Round-trip test asserts property constraints + stages + actions + effects (not just counts)

---

#### C′.9 — Tests expansion (required for C exit)

Minimum additional tests:

- [ ] **C′.9.1** Constraints: required, unique, range(min open), length, pattern
- [ ] **C′.9.2** Policy + require (after C′.1)
- [ ] **C′.9.3** Subscription parse
- [ ] **C′.9.4** Multi-entity + relationship
- [ ] **C′.9.5** `is not null` expression shape
- [ ] **C′.9.6** Malformed input (missing brace) — diagnostic or clear error with line/col
- [ ] **C′.9.7** Prefer `DomainFactory.Create` / bootstrap with built-ins as evolve base (not empty domain without types)

---

#### C′ exit

- [ ] C′.1–C′.5 required items green
- [ ] C′.6 grammar matches code
- [ ] C′.7 diagnostics story for agents
- [ ] C′.8–C′.9 tests green
- [ ] Multi-entity SupplyChain-thin fixture round-trips structurally (entities, props, constraints, stages, actions, effects, relationships, subscriptions)
- [ ] No fake always-true policies for gates

**Then:** Slice D (`apply_dsl` MCP) may start.

---

### Slice D: MCP — `apply_dsl` + dual path

- [ ] **D.1** `apply_dsl` tool: accept `.poly` text → parse → evolve → analyze → return diagnostics + snapshot. **Strict only** (analysis errors reject commit).
- [ ] **D.2** Wire through `McpSessionStore` + `Poly.Mcp/Tools/DomainTools.cs`.
- [ ] **D.3** Affordances: prefer DSL/batch after success, but **retain** micro-tools (`add_entity`, `add_property`, `add_stage`, `add_action`, …) for discovery and repair.
- [ ] **D.4** Optional later: block-level tools (`add_entity_block`) as convenience — **not** a requirement to delete micro-tools.
- [ ] **D.5** Capture mode for `apply_dsl` is **out of this slice** (honesty / reverse-engineering workstream — see MCP expansion plan).
- [ ] **D.6** Tool descriptions must not claim Slice B runtime execution until a dedicated execute/simulate tool exists and is true.
- [ ] **D.7** Update MCP README: dual path documented.

**Slice D exit:** Strict `apply_dsl` works; micro-tools still registered; docs honest.

---

### Slice E: Phase 1b grammar (pull-only)

Only when a named consumer needs value types, `create in`, or quantifiers.

- [ ] Extend grammar doc + parser + printer + tests.
- [ ] Runtime support for any new effect used in the green path (do not parse what cannot be analyzed/executed without honesty flags).

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
| Relationship dual form forever | Medium | Medium | Choose N1/N2 in Slice A; don’t leave undocumented |
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
| C Phase 1a scaffold | — | Partial (tokenizer/parser/printer + 3 tests) |
| **C′ Parser correctness** | **1–3 days** | **Current pick** |
| D MCP `apply_dsl` | 2–4 days | After C′ |
| E Phase 1b | Pull-only | Sized by consumer |

**Do not** plan this as a single 10–15 calendar-day completion of A–D. Sequence slices; land green exits.

**Dependency summary:**

```text
0 → … → B+B′ → C scaffold → C′ → D → E
                         ↘ BR residual (optional)
You are here: C′
```

---

## 7. Exit criteria (program done when all applicable slices green)

### Slice A (+ A′ + A′′)

- [x] No authorable `Event`, `EventSubscription`, `EventCorrelationBinding`, `EventSubscriptionRoutingMode`, or `PublishEventEffect` on the product path.
- [x] Stage subscriptions on `Stage`; event analyzers removed; `DMSS*` passes registered.
- [x] ADR + CORE updated.
- [x] **A′ core:** semantic remove; real contract analyzer; builder subscribe; subscription tests.
- [x] **N2 interim** for relationships (first-class records); N1 deferred to Slice C.
- [x] **A′′:** fail-loud zero-match remove; query/MCP subscription visibility; duplicate-key warning; OneToOne quantifier check.

### Slice B + B′

- [x] **CallAction** → effects → stage → `when` (Each) → subscriber side effects (literal assign).
- [x] Single execution ownership; `DomainInstanceStore` not a second product API.
- [x] B′.1 / B′.3 / B′.4 / B′.5 done.
- [ ] B′.2 / BR.1: event property flow tested or Option B documented.
- [ ] BR.3: entry/exit, stage gates, auto-Add children (optional).

### Slice C + C′

- [x] Phase 1a grammar doc in-repo (`dsl-phase1a-grammar.md`) — needs C′.6 consistency pass.
- [x] Parser/printer scaffold exists; 3 minimal tests pass.
- [ ] **C′:** require/when semantics, `is not`, pattern, primitives once, relationships N2, diagnostics, printer honesty, expanded tests.
- [ ] Expressions map correctly (`is not` → NotEqual).

### Slice D

- [ ] Strict `apply_dsl` MCP tool.
- [ ] Micro-tools retained; dual path documented.
- [ ] No false capability claims.

### Cross-cutting

- [ ] Test suite green after each slice (count may change).
- [ ] Work broken into `simple-agent-tasks` when agents execute.
- [ ] `AGENTS.md` principles unchanged unless a principle itself changes (rare).

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
| 2026-07-17 | C **scaffold reviewed** (untracked Parsing/*): grammar + tokenizer + parser + printer + 3 tests. **C′** added — fake require/when policies; `is not` bug; pattern token bug; EnsurePrimitives duplicates; no relationship syntax; throws not diagnostics; weak tests. |

### Appendix — Relationship authoring (N2 interim)

**Accepted through Slice B:** Relationships are first-class `Relationship` records on `Domain`, authored via `AddRelationshipChange` / builders / MCP relationship tools. `StageSubscription.RelationshipName` resolves with `Source.TypeName == subscriber entity` and `Name == RelationshipName`.  

**Runtime (thin):** Matching is **type-level** until an instance link store exists (BR.4.4).  

**DSL Phase 1a (C′.5):** Must emit N2 **explicit** relationship syntax (not property-line N1). Property-line form remains deferred to later Phase 1b/N1.

### Appendix — Agent pick order (after C scaffold review)

| Order | Task | Severity | Blocks |
|-------|------|----------|--------|
| 1 | **C′.1** require/when real semantics (no Literal(true) stubs) | Bug | Honest policies |
| 2 | **C′.2** `is not` → NotEqual | Bug | Policy expressions |
| 3 | **C′.3** pattern() string capture | Bug | Constraints |
| 4 | **C′.4** primitives once / DomainFactory base | Bug | Multi-entity files |
| 5 | **C′.5** N2 relationship parse/print + subscription fixture | Gap | when subscriptions |
| 6 | **C′.6–C′.9** grammar, diagnostics, printer honesty, tests | Required | C exit |
| 7 | **Slice D** `apply_dsl` MCP | Product | After C′ |
| 8 | **BR.*** runtime polish | Optional | Parallel |

Principles: minimal diffs; TUnit names `Method_Condition_ExpectedResult`; no new abstractions; do not reintroduce Event/Publish; run DomainModeling tests before calling done. Parser must not invent always-true policies for gates.
