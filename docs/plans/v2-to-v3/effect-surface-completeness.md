# Effect Surface Completeness

**Date:** 2026-07-18  
**Revised:** 2026-07-19 (**E6** post-change code review — uncommitted DSL gap closure; suite **1398**)  
**Status:** E1 **shipped**; E2.1 create-in only; E3a self-invoke **DSL+RT shipped**; E4 conditional **DSL+RT shipped**; action params **DSL+RT shipped**; Q1′ authoring **complete**; arithmetic/`equals`/`enum`/inheritance/`owned` **DSL shipped** (suite **1409**)  
**Current pick:** E6 follow-ups (RT goldens + hygiene) **or** query **Q3′** decision — [`dsl-query-surface.md`](dsl-query-surface.md) §15




**Related:**  
- [`dsl-query-surface.md`](dsl-query-surface.md) — **parallel** related **reads** (subject-first; §3.1 reads OK / writes banned) · [`qe-README.md`](simple-agent-tasks/qe-README.md)
- [`mcp-phase3-oracle-surface.md`](mcp-phase3-oracle-surface.md) §6c RT · §6e SA  
- [`mcp-tool-surface-expansion.md`](mcp-tool-surface-expansion.md) §0  
- Product DSL: [`Poly.Mcp/Docs/poly-dsl-agent-guide.md`](../../../Poly.Mcp/Docs/poly-dsl-agent-guide.md)  

**Principle:** Usefulness = **executable × authorable × honest**. Prefer finishing the path for effects that already run over inventing many new effect kinds. No domain VM opcodes for host I/O (email/HTTP) — host adapters later.

---

## 1. Why this plan

Phase 2–4 closed structure, spawn-and-wire, MCP runtime exercise, and stage-action footguns. Dogfood domains (Order/Customer-style) work with a **small** effect set.

Product doubt remains: **have we modeled enough effects to be useful?**  

Honest answer from inventory:

- **IR + InvokeAction** can do more than agents can write.  
- **DSL product path** only authors a subset.  
- **MCP** has almost **no** effect-add micro-tools (effects via `apply_dsl` or C# evolution).

This plan tracks a **parity matrix** and a thin vertical to make the **lifecycle effect language** useful without a completeness catalog.

---

## 2. Parity matrix (source of truth)

Legend: **✅** product-ready · **🟡** partial · **❌** missing · **🚫** non-goal (this plan)

| Effect (IR) | Runtime (`InvokeAction`) | DSL parse/print | Evolution builder | MCP micro-tool | Notes |
|-------------|------------------------|-----------------|-------------------|----------------|-------|
| **StageTransition** | ✅ direct | ✅ `transition to S` | ✅ `AddStageTransitionEffect` | ❌ | Core lifecycle |
| **Assign** | ✅ VM | ✅ `assign P to expr` | ✅ `AddEffectToAction` | ❌ | Core data change |
| **CreateEntity** | ✅ direct | ✅ `create T { }` | ✅ helpers | ❌ | Optional `RelationshipName` auto-link |
| **CreateInRelationship** | ✅ direct | ✅ `create in Rel { }` | 🟡 via `AddEffectToAction` | ❌ | Spawn-and-wire |
| **Composite** | ✅ VM (direct children silently dropped — DMEFF006 warning) | ✅ flatten (children inline) | 🟡 construct | ❌ | Nested structure; only Assign/sub-Composite/sub-Conditional execute via VM; direct effects silently dropped |
| **Conditional** | ✅ VM (direct children silently dropped — DMEFF006 warning) | ✅ `if (expr) { effects } else { effects }` | 🟡 construct | ❌ | Branching; only VM-lowerable children execute; direct effects silently dropped in both then/else |
| **InvokeAction** | 🟡 **self only** | ✅ `invoke ActionName` (+ optional args) | 🟡 construct | ❌ | `InvokeAction(ActionName)` on **this** instance; **ParameterBindings evaluated** (self only) — **not** multi-entity yet |
| **DeleteEntityInstance** | ✅ soft-delete **self** | ✅ `delete` (E1) | 🟡 construct | ❌ | Executor ignores `EntityType`; parser stamps `_currentEntityName`. Soft-delete only |
| **LinkRelationship** | 🟡 constrained | ❌ no DSL | 🟡 construct | ❌ | Target must be `PropertyAccess` whose bag value is already a `DomainEntityInstance`; else throws. Prefer `Store.Link` in tests |
| **UnlinkRelationship** | 🟡 same as link | ❌ no DSL | 🟡 construct | ❌ | Same target resolution rules |
| **TransitionRelationship** | ❌ **not executed** (DMEFF005 warning) | ❌ | 🟡 construct | ❌ | IR exists; **no `case` in `ExecuteEffect`** — analyzer warns on use; do not add DSL until runtime handles it |
| OnEntry / OnExit effects | ✅ on transition | ✅ `entry`/`exit` blocks | ✅ stage effect changes | ❌ | Same effect subset as actions (product path) |
| Stage **when** subscriptions | ✅ store notify | ✅ `when Rel Stages { }` | 🟡 | ❌ | Not an Effect type; related surface |
| Host I/O (email, HTTP, queue) | 🚫 | 🚫 | 🚫 | 🚫 | Post–P3 / host adapters |

**Authoring bottleneck:** rows that are ✅/🟡 runtime but ❌ DSL.  
**Runtime honesty:** rows that are 🟡/❌ at runtime must not get DSL first (rule §4.1).

---

## 3. “Useful enough” bar (lifecycle kernel)

A domain is **useful** for internal process modeling when agents can author and **exercise**:

| Capability | Effect / feature | Status |
|------------|------------------|--------|
| Advance lifecycle | transition | ✅ DSL + RT |
| Mutate fields | assign | ✅ DSL + RT |
| Spawn related work | create / create in | ✅ DSL + RT |
| React to peers | when + store | ✅ DSL + RT |
| Guard | policy / require | ✅ |
| Soft-remove | delete | ✅ DSL `delete` + RT refuse after delete |
| Connect existing instances | link / unlink | 🟡 property-bag target only; or `Store.Link` |
| Call another action on **self** | invoke | 🟡 DSL + RT self; bindings evaluated; multi-entity still ❌ |
| Call action on **related** instance | invoke+nav | ❌ not implemented |
| Branchy effects | if/else | ✅ DSL + RT (VM-lowerable children; DMEFF006 on direct non-VM children) |
| Action parameters | `(name: Type)` | ✅ DSL parse/print; RT binding path exists |

**Kernel bar (dogfood-2):** met for Order/Customer-style.  
**Workflow bar:** partially met (delete + self-invoke + conditional authorable). Still open: multi-entity invoke (E3b) and/or link pain with named dogfood.

---

## 4. Design rules

1. **Runtime first** — do not add DSL for effects that InvokeAction cannot execute.  
2. **DSL before MCP micro-tools** — batch path is proven; micro-tools only where incremental edit pain is real (dogfood).  
3. **One golden domain per slice** — e.g. support ticket: open → assign (link) → escalate (invoke) → close (delete).  
4. **Honesty** — guide + tool Description match parser; no lab keywords.  
5. **SA constraints** — stage-action Option B snapshot limits still apply when placing actions with effects ([§6e](mcp-phase3-oracle-surface.md)).  
6. **No effect soup** — prefer compose of assign/transition/create over one-off “business” effect types.  
7. **Host I/O out of scope** — email/HTTP/payments are not Phase 1a effects.  
8. **Query language is parallel** — customer policies need related **reads** ([`dsl-query-surface.md`](dsl-query-surface.md) §3.1/§4.0); effects alone do not make the DSL ship-ready. Assign never does cross-entity writes.

---

## 5. Slices (execution order)

### E0 — Matrix freeze + guide honesty (**small**)

- [x] **E0.1** Matrix updated when effects change (maintained in this doc).  
- [x] **E0.2** Product guide § effects matches parser — now includes `delete` (E1).  
- [x] **E0.3** Guide documents IR-only effects (link, invoke) under §8 after supported table.  
- [x] **E0.4** Dogfood domain shortlist — deferred to E2/E3 (link/invoke).  
- [x] **E0.5** (**E′.5**) Ticket story clarified: assign = field assign vs graph link — E2 covers link.  
- [ ] **E0.6** (**E′.6**) Optional: legend under matrix — deferred.

**Exit:** Agents can see “supported vs library-only” without reading source.

---

### E1 — Soft-delete product path (**small–medium**)

**Goal:** Author and exercise “close / cancel / archive” without custom C#.

**Runtime truth (E′):** `DeleteEntityInstance` currently only sets `IsDeleted` on **the executing instance**. The `EntityType` field is unused at execution. Product DSL should mean **delete self** (e.g. bare `delete`), not “delete arbitrary type.”

- [x] **E1.0** Spec: `delete` keyword = soft-delete **current** instance. `Delete` token added to tokenizer. Entity self-reference via `_currentEntityName`.  
- [x] **E1.1** DSL: `ParseEffect()` handles `TokenKind.Delete` → `DeleteEntityInstance(new DomainTypeReference(_currentEntityName))`. Printer outputs `delete`.  
- [x] **E1.2** Guide updated: `delete` in Supported Effect Summary table. Printer round-trip via `export_dsl` assertion in golden.  
- [x] **E1.3** Golden test `ApplyDsl_WithDelete_SoftDeletesInstance`: DSL → apply → create instance → call Archive → InvokeAction refused afterward. Validate `export_dsl` contains `delete`.  
- [x] **E1.4** MCP not required — DSL suffices. Existing `RuntimeTool.InvokeAction` + `IsDeleted` check handles the runtime path.

**Exit:** Soft-delete is first-class on product path; RT′.6 remains correct. **Met** (`121cd92`, suite **1360**).

### E1′ / E1′′ — closed at commit `121cd92`

Honesty nits (error string, guide soft-delete/unlink/TRE, entry/exit) landed with E1. Commit complete.

### E1′′′ — post-commit review (2026-07-18)

**Scope:** `121cd92` + `7d0f1af` plan pointer commit. Working tree **clean**. Suite **1360**.

**Verdict:** **Accepted as shipped.** E1 thin vertical is product-complete for self soft-delete. No blocking code follow-ups. Next product work is **not more delete** — it is **Q0/Q1′** (subject-first related reads) and/or **E2.1** (link decision).

**Solid (shipped)**

| Item | Notes |
|------|--------|
| Token / parse / print | `delete` → self `DeleteEntityInstance` → `delete` |
| Error text | includes `delete` |
| Guide | supported + soft-delete note + library-only (link/unlink/invoke/TRE) |
| Golden | apply → export → create → Archive → refuse |
| Query plan | `dsl-query-surface.md` committed alongside |

**Residuals**

| ID | Severity | Finding |
|----|----------|---------|
| **E1′′′.1** | Low | Optional guide note: `delete` is a reserved keyword (cannot name types `delete`). |
| **E1′′′.2** | Low | `DeleteEntityInstance.EntityType` still unused at execute — accept stamp or cleanup later. |
| **E1′′′.3** | Low | No dedicated fail-loud test for bad effect token error string (only happy path). Optional. |
| **E1′′′.4** | Low | Matrix already shows delete ✅ DSL — keep E0.1 discipline on future effect PRs. |
| **E1′′′.5** | **Next** | **E2.1** — record create-in-only vs bag/param link decision in § decision log. |
| **E1′′′.6** | **Parallel** | **Q0** honesty; **Q1′** subject-first related **reads** (`Rel exists`, path-prefix, `where`) — [`dsl-query-surface.md`](dsl-query-surface.md) §3.1/§4.0. |
| **E1′′′.7** | Pull | E3a/E3b invoke; TRE runtime-or-hide; ParameterBindings. |
| **E1′′′.8** | Process | Prefer one agent pick: either E2.1 **or** Q0 first if parallel thrash is a risk — default **Q0** if customer ship = policies, **E2.1** if graph write is the pain. |

**Checklist**

- [x] **E1′′.1** Commit `121cd92`  
- [ ] **E1′′′.1–.3** Optional hygiene (reserved keyword note; EntityType cleanup; error-string smoke)  
- [x] **E1′′′.4** Matrix delete row ✅ (maintain on future PRs)  
- [x] **E1′′′.5** E2.1 decision — create-in only  
- [x] **E1′′′.6** Q0/Q1′ (qe suite) — Q0.1–Q0.5 + E2.1 in progress  
- [ ] **E1′′′.7–.8** Pull / pick discipline  

**Recommended:** Stop E1. Start **qe** suite **Q0.1** (guide honesty) → Q1′ subject-first reads; **E2.1** parallel after Q0.1–Q0.2. Do not open multi-entity invoke DSL yet.

---

### E2 — Link / unlink product path (**medium**)

**Goal:** Connect existing instances from domain effects, not only create-in or `Store.Link` from tests.

**Runtime truth (E′):** `Link`/`Unlink` require `Store` and a target that is a **property bag entry holding a `DomainEntityInstance`** (PropertyAccess only). That is a high bar for DSL; create-in remains the easier spawn path.

- [x] **E2.1** Decision: **(a) create-in only**. See § Decision Log.  
- **Skip** **E2.2–E2.5**: Link/Unlink DSL deferred — create-in is the product graph-write path. Reopen only with named dogfood pain.  

**Exit:** Documented decision + explicit non-goal with create-in as substitute.

**Note:** Do not pretend compile-time entity type names are runtime instances.

---

### E3 — Invoke product path (**medium**)

**Goal:** Nested / multi-entity workflows without a second agent `invoke_action`.

**Runtime truth (E′):** Today `InvokeActionEffect` only does `InvokeAction(ActionName, args)` on **this** instance. **ParameterBindings are evaluated** (args map fully wired). Multi-entity invoke is **new runtime work**, not “just un-reject DSL.”

- [x] **E3.0** Split product goals:
  - **E3a** Self-invoke / re-entrancy — **DSL shipped** (`invoke Name`, optional bindings).  
  - **E3b** Invoke on related instance (nav/link path) — **runtime + DSL** still open.  
- [x] **E3.1** E3a: ParameterBindings used; IR kept. E3b still open.  
- [x] **E3.2** E3a DSL keyword + printer + guide.  
- [ ] **E3.3** Guard recursion / re-entrancy (OnEntry → invoke → transition) — **open** (see E6.2).  
- [x] **E3.4a** Golden: MCP apply/export smoke for `invoke` (authoring).  
- [x] **E3.4b** Golden: E3a **runtime** self-invoke via `create_instance` → `invoke_action` (E6.1).  
- [ ] **E3.4c** Golden: E3b parent→child if/when in scope.  
- [x] **E3.5** Guide honesty: self-only invoke documented; multi-entity not claimed.

**Exit:** E3a authoring **met**. E3a RT exercise + E3b still open — do not claim multi-entity until E3b.

---

### E4 — Conditional / composite authoring (**medium**)

- [x] **E4.1** DSL sugar: `if (expr) { effects } [else { effects }]`.  
- [x] **E4.2** Printer round-trip (real `if`/`else`, not flattened comment).  
- [x] **E4.3** Goldens: MCP apply/export smoke for conditional assign.  
- [x] **E4.4** Runtime golden: branch taken/not-taken under `invoke_action` (E6.1).  
- [ ] **E4.5** Optional: `else if` sugar (parser currently requires nested `if` inside `else`).

**Exit:** Branchy actions authorable without hand-built IR — **met** for authoring. RT exercise still open (E6.1).

---

### E5 — MCP thin effect tools (**small**, pull with dogfood)

Only if agents refuse full `apply_dsl` for incremental effect edits after E1–E3:

| Tool (sketch) | Wraps |
|---------------|--------|
| `add_transition_to_action` | `AddStageTransitionEffect` |
| `add_assign_to_action` | `AssignEffect` |
| `add_create_in_to_action` | `CreateEntityInRelationshipEffect` |

- [ ] **E5.1** Named dogfood pain (agent quotes).  
- [ ] **E5.2** One tool vertical + smoke.  
- [ ] **E5.3** Descriptions respect SA snapshot rules for stage actions.

**Exit:** Not a full effect catalog — only proven thin wrappers.

---

### E pull / non-goals

| Item | When / why |
|------|------------|
| Email, HTTP, queue, payment effects | Host integration / post–P3 packs |
| Full effect-micro for every IR type | Completeness trap |
| Event publish/subscribe effects | Retired product path |
| Containers / C# codegen for effects | §6d post–Phase 3 |
| Fixing SA stale snapshot (Option A) | phase3 SA′′.4 — separate unless blocked here |

---

## 6. Suggested PR stack

1. **E0** — matrix + guide “IR vs DSL” honesty  
2. **E1** — delete DSL + golden  
3. **E2** — link decision + implement or document substitute  
4. **E3** — invoke product path  
5. **E4** — conditional DSL only if a golden domain demands it  
6. **E5** — MCP thin tools only with dogfood quotes  

---

## 7. Test / dogfood plan

| Check | Asserts |
|-------|---------|
| Parity table | Updated in same change as new effect surface |
| Round-trip | parse → print → parse for each new DSL effect |
| RT smoke | create_instance → call_action → observe side effects |
| Multi-entity | at least one E2 or E3 golden with two instances |
| Negative | unsupported keywords still fail-loud |

**Recommended dogfood domain (hurts on purpose):** support **Ticket**

```text
Ticket: Open → (assign agent / link) → InProgress → (escalate / invoke) → Resolved → (archive / delete)
Customer when tickets Resolved { … }
```

What you cannot write in DSL without this plan is the backlog order.

---

## 8. Success criteria (thin vertical)

- [x] E0 honesty: guide lists `delete` + partial library-only (link, unlink, TransitionRelationship) — soft-delete self noted  
- [x] **E1** green (delete on product path)  
- [x] **E2** decision recorded (create-in only — see § Decision Log)  
- [x] **E3a** self-invoke authorable in DSL + guide honesty  
- [x] **E4** conditional authorable in DSL + round-trip print  
- [ ] One multi-entity workflow green (E2 or E3b) **or** deferred with reason — **deferred** (E2.1 create-in; E3b not started)  
- [x] E3a/E4 **runtime** goldens under MCP RT (E6.1)  
- [x] Suite green (**1398**) with DSL gap-closure goldens  
- [x] No host I/O effects; no event tools  

**“Useful enough” claim:** lifecycle kernel + soft-delete + self-invoke + conditional authorable via DSL. Exercisable end-to-end under MCP RT for invoke/conditional still needs E6.1 goldens. Multi-entity invoke remains E3b.

---

## 9. Decision Log

| Date | ID | Decision | Rationale |
|------|----|----------|-----------|
| 2026-07-18 | **E2.1** | **(a) create-in only** — product graph writes stay explicit. Link/Unlink remain library/test-only. | Link runtime requires a `PropertyAccess` whose bag value is a `DomainEntityInstance` — high bar for DSL, narrow use case. `create in Rel { … }` with optional `RelationshipName` on `create` already handles spawn-and-wire. Cross-entity writes via assign are banned (§3.1 of query-surface). If link pain resurfaces dogfood, reopen with concrete domain scenario. E2.2–E2.4 **deferred**. |
| 2026-07-19 | **E6** | Close IR↔DSL authoring gaps for arithmetic, E3a invoke, E4 conditional, action params, inheritance, equals/enum, owned. RT goldens deferred to **E6.1**. | IR + runtime already supported these; product path was the bottleneck. Approve authoring ship at suite **1398**; do not over-claim RT exercise or E3b. |
| 2026-07-19 | **E6.1-fix** | Action-param bag injection alone is insufficient: unresolved `Member` RHS passthroughs the whole `_values` dictionary into the assign target. Effect compile now uses an action-scoped `TypeDefinitionNodeAnalyzer` that includes declared parameters as dictionary-backed members. | `EmitMember` unresolved fallback returns the instance bag; Label became Dictionary`2.ToString(). Suite **1409**. |

---

## 9. Agent pick (right now)

**Micro-tasks:** [`simple-agent-tasks/qe-README.md`](simple-agent-tasks/qe-README.md) — pick first `[ ]` there.

```text
DONE:    E1; E2.1; E3a DSL+RT; E4 DSL+RT; params DSL+RT; E6.1–E6.10; Q1′ authoring; arithmetic/equals/enum/inheritance/owned DSL
CURRENT: query Q3′ decision OR dogfood
THEN:    E3b only with named multi-entity pain
LATER:   E5 micro-tools; optional RT eval related policies
PULL:    Host I/O; micro-catalog; L*; TRE; link DSL; E6.11–E6.12
```

**Implementer watch-outs**

- **`delete` = soft-delete self**; entry/exit allowed (guide).  
- Do not DSL non-executed IR (TransitionRelationship).  
- **Invoke is self-only** until E3b — DSL already authors E3a only.  
- Link targets = instance-valued properties; create-in is the easy graph write.  
- **Query surface:** cross-entity **reads** legal; **writes** banned via assign — [`dsl-query-surface.md`](dsl-query-surface.md) §3.1 · §4.0.  
- **get_dsl_guide** embed-only — rebuild after guide edits.  
- Authoring goldens ≠ RT goldens — E6.1 closes the gap for invoke/conditional/params.

---

## 10. E′ — Plan review (2026-07-18)

**Scope reviewed:** `effect-surface-completeness.md` (new) + plan pointers + small SA fallthrough Description nit in `DomainTools.cs`.  
**Code changes in flight:** Description-only (SA′′.2); no E0–E1 product code yet.

### Verdict

Plan direction is **right** (authorability of effects that already run). Initial matrix **overstated** Invoke / TransitionRelationship / Delete semantics. E′ corrections above must stay in the matrix before implementers un-reject `invoke` or invent link syntax.

**Solid**

| Item | Notes |
|------|--------|
| Usefulness framing | executable × authorable × honest |
| Kernel vs workflow bars | Matches dogfood reality |
| E0 before E1 | Guide honesty first |
| E2 hard problem named | Instance identity is runtime |
| E5 gated on dogfood | Avoids micro-tool catalog thrash |
| Non-goals | Host I/O, events, containers |

### Residuals / follow-ups

| ID | Severity | Finding |
|----|----------|---------|
| **E′.1** | **High (plan honesty)** | Matrix fixed in this revision: Invoke = self only, params ignored; TransitionRelationship **not executed**; Delete = self soft-delete. Keep matrix honest. |
| **E′.2** | Medium | E3 split into **E3a self** vs **E3b related** — do not ship multi-entity DSL without E3b runtime. |
| **E′.3** | Medium | E1 must specify **delete self** (E1.0); `EntityType` on IR is currently dead at execute. |
| **E′.4** | Medium | E2 default recommendation lean **(a) create-in only** unless bag/param story is designed first — link runtime is narrow. |
| **E′.5** | Low | Ticket dogfood “assign agent / link” may be **assign fields** not graph link — clarify in E0.4 so dogfood doesn’t demand E2 prematurely. |
| **E′.6** | Low | Evolution column “🟡 construct” is vague — means “can `AddEffectToAction` with hand-built effect,” not first-class fluent helpers for all kinds. |
| **E′.7** | Ops | Stage/commit plan pointer files; keep §0 pick on E0→E1. |
| **E′.8** | Low | Optional: note entry/exit only contain product effect subset once E1+ land. |
| **E′.9** | Pull | `TransitionRelationshipEffect` — implement runtime or delete/hide IR (do not DSL). |
| **E′.10** | Pull | Wire `InvokeActionEffect.ParameterBindings` when E3 needs args. |

**Checklist**

- [x] **E′.1** Matrix accuracy corrections  
- [x] **E′.2–.4** Reflected in E1–E3 slice text  
- [ ] **E′.5** Clarify Ticket dogfood “assign” vs graph link in E0.4  
- [ ] **E′.6** Optional legend for evolution column  
- [x] **E′.7** Plan package committed (`08994cc`)  
- [ ] **E′.8–.10** As product work lands  

**dsl-query-surface.md** parallel plan (Q0→Q1′; §3.1/§4.0 frozen).  

**Also in working tree (not effect surface code):** `add_action_to_stage` Description now mentions InvokeAction **fallthrough** (SA′′.2) — good; include with next SA/docs commit if still unstaged.

**Recommended:** Start **E0** (guide IR-vs-DSL section) using the corrected matrix; then **E1.0+E1.1** delete-self. Defer E2 implementation until decision E2.1(a) vs (c); treat E3b as explicit runtime project.

---

## 11. E′′ — Working-tree review (2026-07-18)

**Scope:** Staged/unstaged **plan package** (`effect-surface-completeness.md` + roadmap pointers) + **MCP honesty nits** in `DomainTools.cs` (no E0–E1 parser/runtime product yet).  
**Suite:** **1359** green.

### Verdict

**Ship the plan package.** Effect-surface plan (with **E′** matrix honesty) is the right next track. Code nits in this tree are small and orthogonal (or supporting). Do **not** claim E0/E1 product-complete — only planning + honesty polish.

### Solid

| Item | Notes |
|------|--------|
| Plan structure | Matrix → bars → E0–E5 → non-goals → pick |
| **E′** matrix | Invoke self-only; delete self; TransitionRelationship not executed; link bag targets |
| E1.0 / E3a vs E3b | Prevents fake multi-entity / typed delete DSL |
| E2 hard problem | Instance identity called out |
| Pointers | README, master-roadmap, expansion §0, phase3 pick |
| **SA′′.2** | `add_action_to_stage` Description includes FALLTHROUGH |
| **get_dsl_guide** | Embedded-resource only (no filesystem fallback) — fail-loud if missing; tests still green via Poly.Mcp embed |
| Empty suggestions copy | Softened “well-structured” overclaim for empty domains |

### Residuals / follow-ups

| ID | Severity | Finding |
|----|----------|---------|
| **E′′.1** | **Ops** | **Commit** staged plan package + unstaged plan/code honesty nits together (or two commits: plans vs DomainTools). Working tree is split staged/unstaged. |
| **E′′.2** | Product (E0) | **E0.2–E0.3 not done** — product guide still does not list IR-only effects (delete/link/invoke) or “library only” section. That is the first real E\* deliverable. |
| **E′′.3** | Low | E0.4/E0.5 still open — Ticket dogfood “assign vs link” clarification. |
| **E′′.4** | Low | E′.6 evolution-column legend still optional. |
| **E′′.5** | Low | `get_dsl_guide` embed-only: ensure publish/pack always includes `EmbeddedResource`; no fallback means broken pack = hard fail (acceptable if CI embeds). |
| **E′′.6** | Low | Suggestions empty message change is fine; no test asserts old “well-structured” string (smoke still passes). |
| **E′′.7** | Docs | Phase3 agent pick should stay on E0→E1 after plans commit; avoid “all gaps closed” for MCP overall. |
| **E′′.8** | Pull | E1–E3 product code — not started; follow plan order with E′ runtime truths. |

**Checklist**

- [x] **E′′.1** Commit plan package + DomainTools honesty (`08994cc`)  
- [ ] **E′′.2** / **E′′′.1** E0.2–E0.3 product guide IR-vs-DSL section  
- [ ] **E′′.3** E0.4–E0.5 Ticket assign vs link  
- [ ] **E′′.4** Optional matrix evolution legend  
- [ ] **E′′.5** Optional embed note in Poly.Mcp README/csproj  
- [x] **E′′.6** Empty-suggestions wording shipped (no new smoke required)  
- [x] **E′′.7** Superseded by E′′′  
- [ ] **E′′.8** E1+ implementation  

**Recommended:** **E′′.1 commit now** → **E0.2–E0.3** guide honesty using §2 matrix → **E1.0+E1.1** delete-self.

---

## 12. E′′′ — Post-commit review (`08994cc`, clean tree)

**Scope:** Committed effect-surface plan package + DomainTools honesty nits. Working tree **clean**. Suite **1359**.

### Verdict

**Accepted as shipped planning + honesty.** No further code blockers on that commit. **Product E0 is still incomplete:** `poly-dsl-agent-guide.md` still lists only transition/assign/create/create-in and Do-NOT `invoke` — it does **not** yet document library-only runtime effects (delete self, link bag targets, self-invoke, TransitionRelationship dead IR). That gap is the next slice.

### Solid (committed)

| Item | Notes |
|------|--------|
| Plan + E′/E′′ honesty | Matrix, E1–E3 runtime truths, pointers |
| FALLTHROUGH Description | `add_action_to_stage` complete |
| Embed-only `get_dsl_guide` | Fail-loud if resource missing; tests green |
| Empty suggestions copy | Less overclaim |

### Residuals / follow-ups

| ID | Severity | Finding |
|----|----------|---------|
| **E′′′.1** | **Product (E0)** | **E0.2–E0.3 still open** — add guide section: product effects vs IR-only (delete, link, unlink, invoke self, TransitionRelationship not executed). Keep Do-NOT list aligned with parser. |
| **E′′′.2** | Low | **E0.4–E0.5** Ticket dogfood: assign field vs graph link; optional loan/fulfillment. |
| **E′′′.3** | Low | **E0.6** evolution-column legend under matrix. |
| **E′′′.4** | Low | Guide §8 “Supported Effect Summary” should stay in lockstep with E0.2 after any E1+ land (process: E0.1). |
| **E′′′.5** | Ops | Plan header/E′′ checklist still said “commit package” — superseded by `08994cc`; this section is current. |
| **E′′′.6** | Low | Embed-only guide: document in Poly.Mcp README or csproj comment that pack requires EmbeddedResource (optional). |
| **E′′′.7** | Pull | E1 delete-self product path after E0. |
| **E′′′.8** | Pull | E2/E3 per plan; TransitionRelationship runtime-or-hide; invoke ParameterBindings. |

**Checklist**

- [x] **E′′.1** Plan package + honesty nits committed (`08994cc`)  
- [x] **E′′′.1** / **E0.2–E0.3** Product guide IR-vs-DSL section — done in E1′  
- [ ] **E′′′.2** / **E0.4–E0.5** Ticket assign vs link  
- [ ] **E′′′.3** / **E0.6** Optional matrix legend  
- [x] **E′′′.4** Process: matrix+guide same PR as effect changes — practiced in E6 gap closure  
- [ ] **E′′′.6** Optional pack/embed note  
- [x] **E′′′.7** E1 shipped  
- [x] **E′′′.8** E3a/E4 DSL shipped; TRE still pull; bindings live for E3a  

**E1′ closed.** Superseded by E6 gap-closure review below.

---

## 13. E6 — DSL gap-closure code review (2026-07-19)

**Scope:** Uncommitted working tree closing “IR exists but not in product DSL” gaps:

| Area | Files |
|------|--------|
| Tokenizer | `PolyDslTokenizer.cs` — `+ - * /`, `if`/`else`, `invoke`, `equals`, `enum`, `owned` |
| Parser | `PolyDslParser.cs` — arithmetic precedence, invoke/if effects, action params, inheritance header, equals/enum, owned nav, primitive-as-property-name |
| Printer | `DomainDslPrinter.cs` — parent entity, params, if/else, invoke, equals/enum |
| Runtime | `DomainInstanceStore.cs` — Any/All quantifier dispatch |
| Analysis | `SubscriptionContractAnalyzer.cs` — this/event property binding validation |
| Validation | `MutualExclusionRule.cs` — MaxAllowed > 1 combinatorics fix |
| Tests | MCP goldens (6) + round-trip additions; suite **1398** |
| Docs | `poly-dsl-agent-guide.md` + this matrix |

**Build/test:** clean build; **1398 passed / 0 failed**.

### Verdict

**Approve for commit as authoring-complete product surface expansion.** Changes correctly re-use existing IR/evolution (`InvokeActionEffect`, `ConditionalEffect`, `AddParameterToActionChange`, `SetEntityParentChange`, arithmetic DE nodes). Guide and matrix honesty match parser. No architectural boundary violations.

**Do not claim:** full RT exercise of invoke/conditional/params under MCP `invoke_action` (authoring goldens only); multi-entity invoke; true related-policy VM eval.

### Solid (shipped in this tree)

| Item | Notes |
|------|--------|
| E3a DSL | `invoke Name[(bindings)]` parse + print + guide |
| E4 DSL | real `if`/`else` parse + print (no comment stub) |
| Arithmetic | `ParseAdd`/`ParseMultiply`; printer already had DE cases |
| Action parameters | `(name: Type)` before `{`; evolution order correct |
| Entity inheritance | `Child: Parent entity { }` + `SetEntityParentChange` |
| equals / enum | constraint keywords + printer |
| owned token | `TokenKind.Owned` in nav lines |
| Any/All notify | store quantifier dispatch matches analyzer contract |
| Subscription bindings | this/event property existence warnings |
| MutualExclusion | MaxAllowed > 1 fixed |
| MCP goldens | arithmetic, invoke, conditional, equals, enum, inheritance |
| Guide honesty | shipped vs not-yet lists updated |

### Residuals / follow-ups

| ID | Severity | Finding | Suggested action |
|----|----------|---------|------------------|
| **E6.1** | **Medium** | Authoring goldens only for invoke / conditional / action params. No MCP RT path: `apply_dsl` → `create_instance` → `invoke_action` → assert side effects (branch taken, nested action ran, param visible). | Add 2–3 RT goldens in `McpSmokeTests` (or Runtime tests). Closes E3.4b / E4.4. |
| **E6.2** | **Medium** | E3.3 re-entrancy / recursion guard for `invoke` (OnEntry → invoke → transition loops) not implemented or golden’d. | Spec depth/cycle policy; fail-loud or depth limit; one negative golden. |
| **E6.3** | Low | Direct `PolyDslRoundTripTests` missing combined golden (params + owned + inheritance + if + invoke in one domain). MCP path covers pieces. | Optional single structural round-trip. |
| **E6.4** | Low | `else if` not sugar — must nest `if` inside `else`. Guide does not show `else if`. | Document nested form **or** add `else if` token path. |
| **E6.5** | Low | `PrintLiteralValue` / string constraints: no escape for embedded quotes in `equals("…")` / pattern. Fine for current goldens. | Escape or reject unsafe literals if dogfood hits it. |
| **E6.6** | Low | Subscription binding diagnostics reuse `SubscriptionContractMismatch`. | Optional dedicated diagnostic code for agents. |
| **E6.7** | Low | `"event."` prefix convention duplicated (analyzer vs execution). | Shared constant if surface grows. |
| **E6.8** | Low | Duplicate “Parse effects” comment in `ParseActionBody`. | Cosmetic cleanup. |
| **E6.9** | Ops | `TypeDefinitionProviderCollectionTests` mock hardening is orthogonal — split commit or note in message. | Prefer separate commit if splitting. |
| **E6.10** | Process | Commit this tree; rebuild MCP so embedded guide matches. | `dotnet build Poly.Mcp` after guide edit. |
| **E6.11** | Pull | **E3b** multi-entity invoke — runtime first, then DSL. | Only with named dogfood pain. |
| **E6.12** | Pull | TRE runtime-or-hide; link DSL (E2.1 stands). | Unchanged. |
| **E6.13** | Parallel | Query plan: mark arithmetic **shipped** (was Q2); Q3′ still open — see [`dsl-query-surface.md`](dsl-query-surface.md). | Keep plans in lockstep. |

### Checklist

- [x] **E6.1** RT goldens: invoke self, conditional branch, action param binding, invoke-with-args under MCP Runtime  
- [x] **E6.2** Invoke re-entrancy: `MaxInvokeDepth = 16`; fail-loud + MCP golden  
- [x] **E6.3** Combined structural round-trip (params + owned + inheritance + if + invoke)  
- [x] **E6.4** `else if` sugar (parser + printer collapse + guide)  
- [x] **E6.5** String `\"` / `\\` escape in tokenizer + printer  
- [x] **E6.6** `DMSS004` SubscriptionEffectBinding  
- [x] **E6.7** `SubscriptionEventAccess` shared constant  
- [x] **E6.8** Duplicate “Parse effects” comment removed  
- [x] **E6.9** Orthogonal mock note accepted in-tree (no split required for ship)  
- [x] **E6.10** Guide embed note in Poly.Mcp README; rebuild after guide edit  
- [ ] **E6.11–E6.12** Pull (E3b, TRE, link)  
- [x] **E6.13** Query-surface arithmetic row → shipped (dsl-query-surface.md §16 + matrix)  

**Also fixed while closing E6.1:** action parameter grammar — canonical form is `Name: action (params)` (params after kind, keeps `Name: kind` consistency). Legacy `Name(params): action` still accepted.

**E6.1 runtime fix (param assign):** bag-injecting args was not enough — DSL bare identifiers lower as `PropertyAccess`/`Member`, and unresolved members passthrough the whole bag. `InvokeAction` now compiles effects with an action-scoped type def (entity properties ∪ action parameters). Unit tests: `AssignEffect_FromActionParameter_*`; MCP: `ApplyDsl_ActionParameter_RuntimeBindingVisible`, `ApplyDsl_InvokeNestedWithArgs_RuntimePassesBindings`. Suite **1409**.

**Recommended next pick:** commit this tree · optional §15 query hygiene · **E3b only with dogfood**. Pull items unchanged.
