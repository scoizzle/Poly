# Effect Surface Completeness

**Date:** 2026-07-18  
**Revised:** 2026-07-18 (**E1′′** — **committed** `121cd92`; suite **1360**)  
**Status:** E1+E1′+E1′′ **shipped**; E2/Q\* next  
**Current pick:** **E2.1** link decision and/or **Q0/Q1**  



**Related:**  
- [`dsl-query-surface.md`](dsl-query-surface.md) — **parallel** policies/guards query language (LINQ-inspired subset)  
- [`mcp-phase3-oracle-surface.md`](mcp-phase3-oracle-surface.md) §6c RT · §6e SA  
- [`mcp-tool-surface-expansion.md`](mcp-tool-surface-expansion.md) §0  
- Product DSL: [`Poly.Mcp/Docs/poly-dsl-agent-guide.md`](../../../Poly.Mcp/Docs/poly-dsl-agent-guide.md)  

**Principle:** Usefulness = **executable × authorable × honest**. Prefer finishing the path for effects that already run over inventing many new effect kinds. No domain VM opcodes for host I/O (email/HTTP) — host adapters later.

---

## 1. Why this plan

Phase 2–4 closed structure, spawn-and-wire, MCP runtime exercise, and stage-action footguns. Dogfood domains (Order/Customer-style) work with a **small** effect set.

Product doubt remains: **have we modeled enough effects to be useful?**  

Honest answer from inventory:

- **IR + CallAction** can do more than agents can write.  
- **DSL product path** only authors a subset.  
- **MCP** has almost **no** effect-add micro-tools (effects via `apply_dsl` or C# evolution).

This plan tracks a **parity matrix** and a thin vertical to make the **lifecycle effect language** useful without a completeness catalog.

---

## 2. Parity matrix (source of truth)

Legend: **✅** product-ready · **🟡** partial · **❌** missing · **🚫** non-goal (this plan)

| Effect (IR) | Runtime (`CallAction`) | DSL parse/print | Evolution builder | MCP micro-tool | Notes |
|-------------|------------------------|-----------------|-------------------|----------------|-------|
| **StageTransition** | ✅ direct | ✅ `transition to S` | ✅ `AddStageTransitionEffect` | ❌ | Core lifecycle |
| **Assign** | ✅ VM | ✅ `assign P to expr` | ✅ `AddEffectToAction` | ❌ | Core data change |
| **CreateEntity** | ✅ direct | ✅ `create T { }` | ✅ helpers | ❌ | Optional `RelationshipName` auto-link |
| **CreateInRelationship** | ✅ direct | ✅ `create in Rel { }` | 🟡 via `AddEffectToAction` | ❌ | Spawn-and-wire |
| **Composite** | ✅ VM | 🟡 flatten/comment | 🟡 construct | ❌ | Nested structure |
| **Conditional** | ✅ VM | 🟡 weak print | 🟡 construct | ❌ | Branching; no first-class `if` in parser |
| **InvokeAction** | 🟡 **self only** | ❌ `invoke` unsupported keyword | 🟡 construct | ❌ | `CallAction(ActionName)` on **this** instance; **ParameterBindings ignored** today — **not** multi-entity yet |
| **DeleteEntityInstance** | ✅ soft-delete **self** | ✅ `delete` (E1) | 🟡 construct | ❌ | Executor ignores `EntityType`; parser stamps `_currentEntityName`. Soft-delete only |
| **LinkRelationship** | 🟡 constrained | ❌ no DSL | 🟡 construct | ❌ | Target must be `PropertyAccess` whose bag value is already a `DomainEntityInstance`; else throws. Prefer `Store.Link` in tests |
| **UnlinkRelationship** | 🟡 same as link | ❌ no DSL | 🟡 construct | ❌ | Same target resolution rules |
| **TransitionRelationship** | ❌ **not executed** | ❌ | 🟡 construct | ❌ | IR exists; **no `case` in `ExecuteEffect`** — do not add DSL until runtime handles it |
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
| Call another action on **self** | invoke | 🟡 IR/runtime self; DSL rejects; params unused |
| Call action on **related** instance | invoke+nav | ❌ not implemented |

**Kernel bar (dogfood-2):** met for Order/Customer-style.  
**Workflow bar:** not met until delete (+ link and/or true multi-entity invoke) are authorable **and** runtime semantics match the marketing.

---

## 4. Design rules

1. **Runtime first** — do not add DSL for effects that CallAction cannot execute.  
2. **DSL before MCP micro-tools** — batch path is proven; micro-tools only where incremental edit pain is real (dogfood).  
3. **One golden domain per slice** — e.g. support ticket: open → assign (link) → escalate (invoke) → close (delete).  
4. **Honesty** — guide + tool Description match parser; no lab keywords.  
5. **SA constraints** — stage-action Option B snapshot limits still apply when placing actions with effects ([§6e](mcp-phase3-oracle-surface.md)).  
6. **No effect soup** — prefer compose of assign/transition/create over one-off “business” effect types.  
7. **Host I/O out of scope** — email/HTTP/payments are not Phase 1a effects.  
8. **Query language is parallel** — customer policies need [`dsl-query-surface.md`](dsl-query-surface.md); effects alone do not make the DSL ship-ready.

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
- [x] **E1.3** Golden test `ApplyDsl_WithDelete_SoftDeletesInstance`: DSL → apply → create instance → call Archive → CallAction refused afterward. Validate `export_dsl` contains `delete`.  
- [x] **E1.4** MCP not required — DSL suffices. Existing `RuntimeTool.CallAction` + `IsDeleted` check handles the runtime path.

**Exit:** Soft-delete is first-class on product path; RT′.6 remains correct. **Met in code** (suite **1360**); **commit still open**.

### E1′ — honesty nits (addressed in working tree)

| ID | Status |
|----|--------|
| **E1′.2** | ✅ Error string includes `delete` |
| **E1′.3** | ✅ Guide: soft-delete self + link/unlink + TransitionRelationship dead IR |
| **E1′.4** | ✅ Guide table: action, entry, exit for `delete` |

### E1′′ — re-review after honesty fixes (2026-07-18)

**Scope:** Full E1 + E1′ honesty. Suite **1360**. Tree **dirty** (E1 code unstaged; `dsl-query-surface.md` staged only).

**Verdict:** **Shipable — commit now (E1′′.1).** Do not claim “shipped” until commit. Plan header “E1′ all closed” overstated **commit**.

**Solid:** parse/print/token; error message; guide honesty; golden soft-delete E2E; RT IsDeleted refuse.

**Residuals**

| ID | Severity | Finding |
|----|----------|---------|
| **E1′′.1** | **Ops** | **Commit** all E1 code + effect-surface plan + query-surface plan together |
| **E1′′.2** | Low | Optional: guide note that `delete` is a reserved keyword |
| **E1′′.3** | Low | `EntityType` on IR still unused at execute — cleanup pull |
| **E1′′.4** | Low | E0.4 dogfood shortlist still soft/deferred |
| **E1′′.5** | Process | Rebuild embed after guide edit (`get_dsl_guide`) |
| **E1′′.6** | Next | **E2.1** link decision |
| **E1′′.7** | Parallel | **Q0/Q1** query surface |
| **E1′′.8** | Pull | E3 invoke; TransitionRelationship runtime-or-hide |

**Checklist**

- [x] **E1′′.1** Commit — `121cd92`  
- [ ] **E1′′.2–.5** Optional hygiene  
- [ ] **E1′′.6–.8** Next slices / pull  

**Recommended:** **Commited `121cd92`. All code + plans staged + committed.** Next: E2.1 link decision and/or Q0.

---

### E2 — Link / unlink product path (**medium**)

**Goal:** Connect existing instances from domain effects, not only create-in or `Store.Link` from tests.

**Runtime truth (E′):** `Link`/`Unlink` require `Store` and a target that is a **property bag entry holding a `DomainEntityInstance`** (PropertyAccess only). That is a high bar for DSL; create-in remains the easier spawn path.

- [ ] **E2.1** Spec: expression form for target instance in DSL. Decide:
  - **(a)** document create-in / `RelationshipName` as the **only** product graph-write path (link = library/test), or  
  - **(b)** bag-based link: require prior assign of instance-valued property (awkward but matches runtime), or  
  - **(c)** parameter-bound link once action params are product-authorable and bound into bag/expressions.  
- [ ] **E2.2** If (b)/(c): implement parse → evolve → CallAction → store end-to-end.  
- [ ] **E2.3** Golden multi-instance: create A, B, link, observe subscription if applicable.  
- [ ] **E2.4** Unlink symmetric if link ships.  
- [ ] **E2.5** Record decision in this file §11 decision log.

**Exit:** Documented decision + either working path or explicit non-goal with create-in as substitute.

**Note:** Do not pretend compile-time entity type names are runtime instances.

---

### E3 — Invoke product path (**medium**)

**Goal:** Nested / multi-entity workflows without a second agent `call_action`.

**Runtime truth (E′):** Today `InvokeActionEffect` only does `CallAction(ActionName)` on **this** instance. **`ParameterBindings` are ignored.** Multi-entity invoke is **new runtime work**, not “just un-reject DSL.”

- [ ] **E3.0** Split product goals:
  - **E3a** Self-invoke / re-entrancy (may already be enough for some workflows) — DSL + goldens only.  
  - **E3b** Invoke on related instance (nav/link path) — **runtime + DSL**.  
- [ ] **E3.1** Spec for E3a vs E3b; wire ParameterBindings or drop dead IR.  
- [ ] **E3.2** DSL keyword only after E3a/E3b semantics match.  
- [ ] **E3.3** Guard recursion / re-entrancy (OnEntry → invoke → transition).  
- [ ] **E3.4** Golden: E3a self-invoke; E3b parent→child if in scope.  
- [ ] **E3.5** Guide honesty: self vs related.

**Exit:** Documented E3a and/or E3b green under MCP RT — do not claim multi-entity until E3b.

---

### E4 — Conditional / composite authoring (**medium**, after E1–E3 if needed)

- [ ] **E4.1** DSL sugar for conditional effects (if product needs branchy actions without C#).  
- [ ] **E4.2** Printer round-trip (stop “flattened comment” only).  
- [ ] **E4.3** Goldens: conditional assign/transition.

**Exit:** Branchy actions authorable without hand-built IR.

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

- [x] E0 honesty: guide lists `delete` + partial library-only (link, invoke, unlink, TransitionRelationship) — soft-delete self noted  
- [x] **E1** green (delete on product path) — suite **1360**  
- [ ] **E2** decision recorded (implement or create-in-only)  
- [ ] One multi-entity workflow green (E2 or E3) **or** deferred with reason  
- [x] Suite green (**1360**) when E1 tests included  
- [x] No host I/O effects; no event tools  

**“Useful enough” claim:** lifecycle kernel + soft-delete + (link **or** invoke) authorable and exercisable via MCP RT without custom C# for the dogfood Ticket (or named substitute domain).

---

## 9. Agent pick (right now)

```text
DONE:    E1 + E1′ + E1′′ committed (suite 1360, commit 121cd92)
CURRENT: E2.1 link decision and/or Q0/Q1 query surface
LATER:   E3a/E3b; E4/E5
PULL:    Host I/O; micro-catalog; L*; TransitionRelationship runtime
```

**Implementer watch-outs**

- **`delete` = soft-delete self**; entry/exit allowed (guide).  
- Do not DSL non-executed IR (TransitionRelationship).  
- **Invoke is self-only** until E3b.  
- Link targets = instance-valued properties.  
- **get_dsl_guide** embed-only — rebuild after guide edits.

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

**dsl-query-surface.md** now tracked as parallel plan (Q0/Q1).  

**Also in working tree (not effect surface code):** `add_action_to_stage` Description now mentions CallAction **fallthrough** (SA′′.2) — good; include with next SA/docs commit if still unstaged.

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
- [ ] **E′′′.4** Process: matrix+guide same PR as effect changes  
- [ ] **E′′′.6** Optional pack/embed note  
- [ ] **E′′′.7–.8** E1+ and pull items  

**E1′ closed.** E2.1 link decision and/or Q0/Q1 query surface next.
