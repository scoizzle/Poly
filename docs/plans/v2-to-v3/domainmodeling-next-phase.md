# DomainModeling Phase 2 — Spawn-and-Wire

**Date:** 2026-07-17  
**Revised:** 2026-07-17 (P2′′ impl review — suite **1319**; **commit only**)  
**Status:** Phase 2 vertical **product-complete uncommitted** (P2.1–P2.5 + P2′ + P2′′ verified)  
**Current pick:** **Commit working tree** — must include untracked `CreateEntityInRelationshipEffect.cs`; then **stop / dogfood**  
**Predecessor:** Phase 1a product-complete ([`dsl-sync-toward-phase1.md`](dsl-sync-toward-phase1.md)); BR.4.4 (`8f46f05`); MR/MR′; N2 dropped  
**Related:** [`dsl-phase1a-grammar.md`](dsl-phase1a-grammar.md), [`docs/CORE.md`](../../CORE.md), AGENTS.md principles  

---

## 1. Why Phase 2 now

Phase 1 closed a **thin vertical**: author structure in DSL/MCP, execute one action path that fires stage subscriptions **when instances are linked**.

The remaining product hole is not grammar completeness. It is:

> **Nothing in the green path creates the instance graph.**  
> Tests hand-call `store.Link`. Agents and demos cannot express “action creates a related entity and wires it so `when` fires.”

That is the named consumer for Slice E (`create` / `create in`) and the natural compound of BR.4.4.

**Phase 2 theme:** *Spawn-and-wire* — create participates in the instance graph; thin Phase 1b DSL surfaces only what the runtime proves; dogfood on a small multi-entity domain.

Principles: domain fidelity, end-to-end ownership, smallest coherent slice, tests grow specific / production grows generic. **Not** “finish all unsupported keywords.”

---

## 2. Phase 1 inventory (do not re-do)

| Shipped | Honest residual (not Phase 2 blockers) |
|---------|----------------------------------------|
| N1 nav only (N2 dropped) | Action `when Stage` parse without runtime enforce |
| CallAction + OnExit/OnEntry + store notify | Composite/Conditional drop nested *direct* effects |
| CreateEntityInstance + optional `RelationshipName` auto-link; `CreateEntityInRelationshipEffect` | Exclusive-owned free create reject analyzed (P2′.2) |
| Link/Unlink effects (target = bag instance) | Delete is flag-only; TRE silent no-op |
| when Each + instance links | Any/All skipped at runtime |
| MCP add_* + remove_* + apply/export_dsl | No MCP CallAction / scenario tool yet |

---

## 3. Phase 2 goal

**One sentence:** An action can create a related entity, auto-register it, **link** it on a named relationship, and a linked subscriber’s `when` fires — authorable via API first, then Phase 1a/1b DSL, then MCP apply_dsl smoke — without hand `store.Link` in the golden path.

**Named consumer:** multi-entity lifecycle domains (Person Die create certificate; Order PlaceOrder create child; Loan checkout). First golden: **thin Order/Customer** with one create-in + one when. Keep Person as structure+policy regression.

**Dogfood domain (behavior):** minimal **Order/Customer** pair with `PlaceOrder` → `create in Places` (or equivalent N1 nav name). Prefer clear relationship topology over Library/ECommerce catalog breadth.

---

## 4. Slice map (execution order)

```text
P2.0  Plan + residual table (this doc)                      [done]
P2.1  CR — Create → Link runtime vertical                   [done — uncommitted]
P2.2  E-create — Phase 1b thin: `create` / `create in` DSL  [done — uncommitted]
P2.3  DF — Dogfood golden: apply_dsl → CallAction → when    [done — uncommitted]
P2′   Honesty residuals                                     [done — uncommitted]
P2.4  E-entry — entry/exit DSL print/parse                  [done — uncommitted]
P2.5  E-when-list — multi-stage `when Rel A, B`             [done — uncommitted]
P2′′  Post–full-impl review polish                          [done — uncommitted]
P2′′′ Post–P2′′ review (hygiene only)                        [commit residual]
P2.x  Defer — Any/All, value DSL, invoke params, TRE, MCP exec
```

### Dependency

| Slice | Depends on | Does not need |
|-------|------------|---------------|
| P2.1 CR | BR.4.4 (done) | DSL |
| P2.2 E-create | P2.1 | value types, Any/All |
| P2.3 DF | P2.1; P2.2 for full DSL path | Library catalog completeness |
| **P2′** | P2.1–.3 | P2.4 |
| P2.4 | P2.2 | CR |
| P2.5 | P2.2 | CR |
| **P2′′** | full P2 | — |

**Phase 2 product vertical complete (uncommitted).** **Only open action: commit** (see P2′′′).

---

## 5. Slice details

### P2.0 — Plan bookkeeping

- [x] This document is the Phase 2 living plan.
- [x] Point [`dsl-sync-toward-phase1.md`](dsl-sync-toward-phase1.md) pick order at Phase 2 / P2.1.
- [x] [`master-roadmap.md`](master-roadmap.md) “What next” points at Phase 2 (dup heading residual → P2′′′.5).

**Exit:** Agents pick **commit**, then dogfood.

---

### P2.1 — CR: Create → Link runtime (**primary vertical**)

**Gap:** `CreateChildInstance` adds to store but does not `Link`. Subscriptions require links → create alone never enables fan-out.

**Design (Option A — preferred):**

1. Extend `CreateEntityInstance` with optional `string? RelationshipName`.
2. After successful create + `Store.Add(child)`, when `RelationshipName` is set and `Store` is present:  
   `Store.Link(rel, this, child)` with creator as **source**, child as **target**  
   (matches N1 source-side nav / SubscriptionContractAnalyzer subscriber-as-source convention).
3. Fail loud: relationship missing on domain → analysis + runtime throw when Domain present (P2′.3 landed).
4. **Do not** invent collection properties or multi-hop.

**Shipped shape:** Option A (`CreateEntityInstance.RelationshipName`) **plus** DSL-facing `CreateEntityInRelationshipEffect` (resolves target from rel at runtime, then create+link).

**Tests (TUnit, `Method_Condition_ExpectedResult`):**

- [x] `CreateEntityInstance_WithRelationship_LinksInStore`
- [x] `CallAction_CreateLinkedChild_SubscriptionFires` (golden 2-entity)
- [x] `CreateEntityInstance_WithoutRelationship_NotLinked` (backward compat)
- [x] `CreateEntityInstance_RelationshipNameWithoutStore_NoOp` (crash safety)
- [x] Existing create-without-rel tests still green (no forced link)
- [x] `CreateEntityInstance_UnknownRelationship_FailsLoud` (P2′.3)

**Exit:** Green golden without test-side `store.Link` after create-with-rel. **Met.**

---

### P2.2 — E-create: Phase 1b thin grammar (`create` / `create in`)

**Only after P2.1 green.**

**Grammar freeze (add to [`dsl-phase1a-grammar.md`](dsl-phase1a-grammar.md) as Phase 1b §):**

```text
create-effect =
    "create" entity-name "{" prop-init* "}"
  | "create" "in" identifier "{" prop-init* "}"   // identifier = relationship / nav name

prop-init = identifier ":" expression
```

- `create Order { … }` → `CreateEntityInstance` without relationship (`RelationshipName` null).
- `create in orders { … }` → `CreateEntityInstance` with `RelationshipName = "orders"` (source = enclosing entity).
- Printer: emit same forms for those effects (today: “not printable in Phase 1a”).
- Remove `create` from “unsupported forever”; keep `schedule` / `parallel` / `for` / `actor` / `value` rejected.

**Tests:**

- [x] Parse → evolve → CallAction golden (`Dogfood_CreateInDSL_SubscriptionFires` in `DomainEntityInstanceTests`)
- [x] Round-trip print/parse structural for create effects (`Parse_CreateEntityEffect_RoundTrips`, `Parse_CreateInEffect_RoundTrips` in `PolyDslRoundTripTests`)
- [x] MCP `ApplyDsl` smoke with create-in + when (`ApplyDsl_WithCreateInAndSubscription_Succeeds` in `McpSmokeTests`)
- [x] Unsupported keywords still reject `schedule` / `value` / etc. (unchanged — `create` removed from unsupported list)

**Exit:** Agent can `apply_dsl` a multi-entity spawn-and-wire domain without C# evolution.

---

### P2.3 — DF: Dogfood golden

**Scenario (recommended):**

```text
Customer ──Places──► Order
Customer.Active.PlaceOrder:
  create in Places { …optional props… }
Order.Draft / Active as needed
Optional: when Places Active { assign … } on Customer stage
```

**Prove:**

1. apply_dsl (or evolution if P2.2 not yet) → domain analyzes clean  
2. store.Add customer → CallAction PlaceOrder  
3. child Order in store, `IsLinked("Places", customer, order)`  
4. if subscription authored, it fires on the linked path  
5. export_dsl still honest  

**Placement:** tests under `Poly.Tests/DomainModeling/`; optional slim demo in `Examples/` only if it stays thin (no Library catalog rewrite).

**Exit:** One documented golden path + green tests. *(README pointer still optional polish.)*

---

### P2′ — Honesty residuals (post–P2.1–P2.3 code review, 2026-07-17)

**Verdict:** Vertical is **product-green** (suite **1306**): runtime create+link, DSL `create` / `create in`, dogfood + MCP apply/export. Shipable after noting analysis gap. Residual is **domain fidelity / agent honesty**, not missing happy path.

| ID | Severity | Finding |
|----|----------|---------|
| **P2′.1** | **Must** | `EffectAnalyzer` has **no** case for `CreateEntityInRelationshipEffect`. Create-in skips unknown-initializer / type resolution checks that `CreateEntityInstance` gets. Also no validation that relationship name exists or that **authoring entity is the relationship source**. Agents can `apply_dsl` garbage that only fails at CallAction (or never, for bad initializers until runtime). |
| **P2′.2** | High (product rule) | **Exclusive ownership not enforced.** Free `create Order { }` still allowed when Order is only ever target of `owned` edges. Discussed design: bare create of exclusively owned types should be analysis error; birth path = `create in` / `RelationshipName` on owned rel. Not implemented. |
| **P2′.3** | Medium | `CreateEntityInstance` with `RelationshipName` calls `Store.Link` **without** checking domain has that relationship (unlike create-in). Unknown name still “links” as opaque string; plan’s `UnknownRelationship_FailsLoud` missing. Prefer fail-loud + analysis (mirror `ValidateRelationshipName` used for Link effects). |
| **P2′.4** | Medium | **Source-entity check** on create-in: runtime resolves rel by name only; action on wrong entity type can create target and link with wrong source. Analysis: creator entity must equal `relationship.Source`. |
| **P2′.5** | Low | Printer: `CreateEntityInstance` with non-null `RelationshipName` prints as bare `create Type {…}`, losing the link channel on round-trip. Prefer print `create in Rel` when `RelationshipName` set (or document dual IR and always author create-in via DSL). |
| **P2′.6** | Low | Plan inventory §2 still half-stale; P2.0 master-roadmap checkbox; DomainModeling README still doesn’t point at spawn-and-wire golden; untracked file must be in commit. |
| **P2′.7** | Optional / pull | P2.4 entry/exit DSL; P2.5 multi-stage when; Composite nesting of create; MCP execute_action. |

- [x] **P2′.1** EffectAnalyzer: validates `CreateEntityInRelationshipEffect` (rel exists, source entity match, resolve target, initializer props) + `CreateEntityInstance.RelationshipName` when set
- [x] **P2′.2** Analysis: rejects bare create of exclusively-owned entity types (only targets of `SourceOwnsTarget` rels, never source)
- [x] **P2′.3** Runtime: `CreateEntityInstance` with unknown `RelationshipName` fails loud at runtime when domain present. Test `CreateEntityInstance_UnknownRelationship_FailsLoud`
- [x] **P2′.4** Source-entity check via `EffectBinding_CreateInWrongSourceEntity_ReportsError`; happy path `EffectBinding_CreateInHappyPath_NoError`
- [x] **P2′.5** Printer: `CreateEntityInstance` with non-null `RelationshipName` prints as `create in RelName { ... }` (round-trip safe)
- [x] **P2′.6** Docs: plan checklists updated; `CreateEntityInRelationshipEffect.cs` tracked; master-roadmap updated
- [x] **P2′.7** Deferred (P2.4 / P2.5 / etc. — pull-only)

**Review notes (positive):**

- Link direction correct (creator source, child target).
- Create-in → resolve target from rel → reuses `CreateChildInstance`.
- Dogfood + MCP apply/export; P2′ analysis cases for create-in; runtime unknown-rel fail-loud.
- Printer `create in` when RelationshipName set; entry/exit + multi-stage when landed.

---

### P2′′ — Post–full-impl review polish — **DONE** (uncommitted; suite **1319**)

| ID | Finding | Status |
|----|---------|--------|
| **P2′′.1** | Exclusive-owned bare create tests | **Done** |
| **P2′′.2** | Create + RelationshipName: source + target type analysis | **Done** (`ValidateCreateWithRelationshipName`) |
| **P2′′.3** | Runtime create-in source check | **Done** (no dedicated runtime unit test — analysis covers wrong source) |
| **P2′′.4** | Dead entry/exit StageTransition branches | **Done** |
| **P2′′.5** | Effect file + phone-call experiment | **Partial** — see P2′′′ |
| **P2′′.6–.7** | Docs / optional completeness | Partial / pull |

**P2′′ impl review (2026-07-17):** Production sound. Analysis depth for create forms is solid. Suite **1319**. **No must-fix before commit** except include the untracked effect source file.

---

### P2′′′ — Commit hygiene + tiny residuals (post–P2′′ review)

**Verdict:** Ship Phase 2. Only residual that blocks a *correct* commit is **file inclusion**. Everything else is optional polish.

| ID | Severity | Finding |
|----|----------|---------|
| **P2′′′.1** | **Must for commit** | `Poly/DomainModeling/Effects/CreateEntityInRelationshipEffect.cs` is still **untracked** (not staged). Commit **must** `git add` this file or the build fails for others. Plan earlier claimed “staged” — **false** as of review. |
| **P2′′′.2** | Low | `phone-call-minimal.poly` untracked experiment (`DateTime.Now`, not Phase 2 grammar). Keep out of product commit or quarantine under experiments without claiming parse green. |
| **P2′′′.3** | Low | Runtime `CreateEntityInstance` + `RelationshipName` still only checks rel **exists** (not source/target type) — create-in has runtime source check; dual path asymmetry. Analysis covers API abuse for both. Optional symmetry only. |
| **P2′′′.4** | Low | No focused **runtime** test that create-in throws on wrong source (P2′′.3 code path). Analysis test exists. |
| **P2′′′.5** | Low | DomainModeling `README.md` still has no spawn-and-wire / create-in pointer; `master-roadmap.md` has a **duplicate** `## Archived migration material` heading. |
| **P2′′′.6** | Optional | Required-prop completeness for create-in; Composite nesting create; MCP execute_action — still non-goals unless dogfood forces. |

- [ ] **P2′′′.1** `git add` `CreateEntityInRelationshipEffect.cs` in the Phase 2 commit
- [ ] **P2′′′.2** Leave phone-call-minimal out of ship claim (or add only as experiment)
- [ ] **P2′′′.3** Optional: runtime source/target checks on create-with-RelationshipName
- [ ] **P2′′′.4** Optional: runtime wrong-source create-in test
- [ ] **P2′′′.5** README + master-roadmap duplicate heading (while editing docs)
- [ ] **P2′′′.6** Pull-only product extras

**Do not open new IR for Phase 2 residual.**

---

### P2.4 — E-entry (done)

Parse/print `entry { effects }` / `exit { effects }` on stages → existing OnEntry/OnExit IR. Runtime already BR.3.

**Implementation:**
- `PolyDslTokenizer`: Added `Entry`, `Exit` token kinds with keyword mapping
- `PolyDslParser.ParseStage`: Parses `entry { effects }` / `exit { effects }` before actions/subscriptions. Error if entry/exit appears after actions/subscriptions.
- `DomainDslPrinter.PrintStage`: Emits `OnEntryEffects` and `OnExitEffects` instead of skipping them.

**Tests:** `Parse_EntryExitEffects_RoundTrips` — full parse → evolve → print → re-parse structural round-trip.

### P2.5 — Multi-stage when list (done)

Parser today: one stage token. IR + runtime already OR-match list.

**Implementation:**
- `PolyDslParser.ParseSubscription`: Accepts comma-separated stage names after `when RelName`.
- `StageSubscription` IR already uses `IReadOnlyList<string> StageNames`, so no IR change needed.

**Tests:** `Parse_MultiStageWhen_RoundTrips` — `when Tracks Active, Done` parse → print → re-parse check.

---

## 6. Explicit non-goals (Phase 2)

| Item | Why defer |
|------|-----------|
| Runtime Any/All | No multi-match consumer yet; analyzer already warns |
| DSL `value { }` | Person values work via builders; not spawn-and-wire |
| Actor / schedule / for / parallel / match | Lab Phase 2+; not this phase’s vertical |
| TransitionRelationshipEffect execution | Second-order bulk transition |
| InvokeAction ParameterBindings | Separate honesty loop |
| MCP `execute_action` / simulate sandbox | After DF proves host API; then thin MCP wrap |
| Library/ECommerce full executable rewrite | Structure catalogs; behavior first on thin domain |
| Capture mode, codegen, multi-domain import | Out of CORE path for this phase |

---

## 7. Success criteria (phase exit)

- [x] P2.1 green: create-with-relationship links; subscription golden without manual Link  
- [x] P2.2 green: `.poly` create / create in round-trips and executes  
- [x] P2.3 green: one multi-entity dogfood path documented + tested (via DSL + MCP apply_dsl)  
- [x] P2.1–P2.3 vertical + dogfood  
- [x] **P2′** analysis + ownership + printer (P2′.1–.6)  
- [x] **P2.4** entry/exit DSL round-trip  
- [x] **P2.5** multi-stage when list round-trip  
- [x] **P2′′** polish landed (exclusive-owned tests, create-with-rel analysis depth, create-in runtime source check, entry/exit parse cleanup)  
- [x] Suite green (**1319**)  
- [ ] **Commit** working tree (**must** include `CreateEntityInRelationshipEffect.cs` — P2′′′.1)

---

## 8. Implementation notes

| Concern | Placement |
|---------|-----------|
| Create + optional link | `DomainEntityInstance.CreateChildInstance`, `CreateEntityInstance` record |
| Analysis | `EffectAnalyzer` — create-in + exclusive-owned bare create (P2′); deepen match rules in P2′′.2 |
| DSL | `PolyDslParser` / `DomainDslPrinter` / `dsl-phase1a-grammar.md` (1b section) |
| Tests | `DomainEntityInstanceTests`, new focused tests, `McpSmokeTests` |
| No new VM opcodes | Create/link stay direct; assign stays VM |

**Watch-outs:**

- Link direction: **source = creator (subscriber side), target = child** for N1 `orders: many Order` on Customer.
- Owned nav (`owned`) does not auto-mean cascade delete — out of scope.
- CompositeEffect still cannot nest direct create — sequential top-level effects on action are enough; do not “fix Composite” unless golden forces it.
- Fingerprint / MCP evolve unchanged unless create tools added (not required if DSL path only).

---

## 9. Suggested PR stack (final)

1. **Commit** entire Phase 2 tree — **`git add` `CreateEntityInRelationshipEffect.cs`** (P2′′′.1)  
2. Stop / dogfood Order–Customer path  
3. **P2′′′.3–.5** only while already in those files  

**Phase 2 product vertical complete (uncommitted).**

---

## 10. Phase 1a-runtime archive (shipped)

BR.4.4 instance graph (Option A) shipped in `8f46f05`:

| Item | Status |
|------|--------|
| IG.0 Link store on `DomainInstanceStore` | Done |
| IG.1 CallAction Link/Unlink effects | Done |
| IG.2 NotifyTransition instance filter | Done |
| IG.3 Golden 2×2 + Unlink tests | Done |
| IG.4 Commit | Done (`8f46f05`) |

Out of scope then (still Phase 2 non-goals or later): multi-hop, Any/All runtime, relationship lifecycle stages, MCP graph query — **create-in auto-link is now in scope as P2.1**.
