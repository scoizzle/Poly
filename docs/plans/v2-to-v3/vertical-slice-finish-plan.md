# V3 Finish Plan — One Vertical Slice at a Time

**Status:** Active  
**Last Updated:** 2026-07-12 — Slice 0 status corrected to match code review (not all Done)  
**Purpose:** Task out **what remains** after V2 delete, ordered as **fully implemented vertical slices** (not work-package breadth).  
**Authority:** Day-to-day execution order for finishing the V2→V3 *product* migration.  
**Related:**

| Doc | Role |
|-----|------|
| [`master-roadmap.md`](master-roadmap.md) | Milestones M1–M4 (delete **done**) |
| [`v3-completion-plan.md`](v3-completion-plan.md) | Historical WP1–WP9 gap inventory |
| [`2026-07-11-review-fix-plan.md`](../2026-07-11-review-fix-plan.md) | Trust layer 1 honesty (feeds Slice 0) |
| [`../decisions/2026-07-11-platform-trust-bar-and-dogfood.md`](../decisions/2026-07-11-platform-trust-bar-and-dogfood.md) | First customer; generation funds platform |
| [`spikes/first-v3-consumer.md`](spikes/first-v3-consumer.md) | MCP + direct API quality bar |
| **[`simple-agent-tasks/vs-README.md`](simple-agent-tasks/vs-README.md)** | **Simple-agent pick queue** (one micro-task at a time) |
| [`simple-agent-tasks/ws8-README.md`](simple-agent-tasks/ws8-README.md) | Older policy tasks (prefer `vs-*`) |

---

## 1. Migration reality check

| Milestone | Status |
|-----------|--------|
| M1 Foundation (evolution, proofs) | ✅ Done |
| M2 First consumer (structure path) | 🟡 **Partial** — bootstrap, evolve, query, MCP structure tools, demos exist; **policy product loop incomplete** |
| M3 V2 freeze | ✅ Done |
| M4 V2 delete | ✅ Done |

**V2 is gone.** “Finish the migration” no longer means porting V2. It means **finish V3 as a trustworthy product path** one **vertical slice** at a time, then pull expressiveness only when a slice or first customer needs it.

```text
Done:   V2 deleted · bootstrap · evolve structure · query · MCP session/structure · DE lower · policy VM tests (core)
Open:   honesty gaps · full policy product loop (API + MCP) · effect execution · derived modules / T2 dogfood
```

---

## 2. Slice protocol (non-negotiable)

### What “fully implemented” means for a slice

Every open slice exits only when **all** of the following are true for that slice’s scope:

| Layer | Requirement |
|-------|-------------|
| **Direct API** | Composable ops on `DomainEvolution` / queries / eval; TUnit proves success **and** failure paths |
| **Runtime truth** | If the slice claims execution (policy, effect), **VM-primary** result is tested (dual-oracle where LINQ is reference) |
| **MCP (if in slice)** | Tool name/description/success match behavior; smoke for the multi-tool agent path |
| **Honesty** | No silent success, no silent wrong ABI, no “claims eval without bool” |
| **Docs** | Slice README blurb or DomainModeling/MCP note matches reality |

### Rules

1. **One open product slice at a time.** Finish exit criteria before starting the next product slice.  
2. **Honesty substrate (Slice 0)** may run first or in parallel *only* where it unblocks the active product slice — do not expand into orphan cleanup mid-slice.  
3. **No breadth flush** (extra MCP tools, Actor, full effect catalog, Validation revive) until the active slice is green.  
4. **Pull-only** after Slice 3: relationships, effects, actors, codegen — only when a named scenario requires them.  
5. Prefer **Person lifecycle** (or Order) as the canonical slice entity — already in demos/tests; do not invent a third demo domain until one is fully green end-to-end.

### Definition of “V3 migration product-complete” (this plan)

Not T2 dogfood. Not every analyzer. **Product-complete for V2→V3** means:

- [x] V2 deleted  
- [ ] Slice 0 honesty for the sold path  
- [ ] Slice 1 structure path reaffirmed green (may already be)  
- [ ] Slice 2 policy runtime on direct API product-enforced  
- [ ] Slice 3 policy MCP agent loop green  

After that: **M2 fully closed**; further work is **WP9 / trust T2**, not “migration.”

---

## 3. Vertical slices (execution order)

```text
Slice 0  Honesty foundation          ── trust layer 1 for sold path
Slice 1  Structure authoring         ── entity · props · stages · actions · query · MCP
Slice 2  Policy runtime (direct API) ── attach + evaluate on CLR record / subject helper
Slice 3  Policy MCP product loop     ── add_policy · evaluate_policy · e2e smoke
──────── M2 product-complete ────────
Slice 4  First effect (optional)     ── one executable effect kind
Slice 5  Relationship (optional)     ── only if product needs second entity link
```

---

### Slice 0 — Honesty foundation

**Why first:** Silent no-ops and dishonest tools make every later slice untrustworthy (trust ADR + review).

**In scope (tasks):**

| ID | Task | Exit check | Status |
|----|------|------------|--------|
| **S0.1** | Fail-loud evolution when entity/stage/action target missing | `RequireUpdate` + rollback | ✅ **Done** |
| **S0.1a** | Surface evalErrors as `EVOLUTION_TARGET` Error diagnostics | Inject before `Diagnostics` Lazy | ✅ **Done** |
| **S0.1b** | Fail-loud missing **stage/property** (child targets) | Child existence check + tests | ✅ **Done** |
| **S0.1c** | RequireUpdate on remaining ApplyTo paths | All Update* ApplyTo use `RequireUpdate` | ✅ **Done** |
| **S0.1d** | Fail-loud remove-by-name zero match *(optional)* | Remove missing child name fails if parent exists | ⬜ Optional — vs **0.1d** |
| **S0.2** | `add_action_to_stage` honesty (create stage-local) | Tool Description + code + test | ✅ **Done** |
| **S0.2a** | MCP README row matches create semantics *(nit)* | Not “places existing” | ⬜ Optional — vs **0.2a** |
| **S0.3** | Wire `PolicySubject` into product evaluate/compile | Dict/Expando rejected | ✅ **Done** |
| **S0.4** | Instance `EmitInvoke` sequences **receiver** | Dual-oracle instance method VM test | ✅ **Done** |
| **S0.5** | MCP README V3-only honesty | No V2 DomainTools claim; tool table complete | ✅ **Done** |

**Status:** ✅ **Required Done.** Optional polish: 0.1d, 0.2a.

**Next:** Slice 1 (verify structure path + pin canonical entity)

**Micro-tasks:** [`simple-agent-tasks/vs-README.md`](simple-agent-tasks/vs-README.md)

---

### Slice 1 — Structure authoring (lifecycle entity)

**Story:** Author a lifecycle-shaped entity end-to-end without policy eval.

```text
create session / DomainFactory
  → add entity → properties → stages → actions → (optional) stage placement
  → get overview / entity detail / analysis
  → bad evolve → rollback + diagnostics
```

| ID | Task | Exit check | Status |
|----|------|------------|--------|
| **S1.1** | Verify structure e2e coverage (inventory + fill gaps) | Checklist + tests green | ✅ **Done** — summary + `GetDomainAnalysis_ReportsNoErrors_ForValidDomain` |
| **S1.2** | Pin **canonical entity** for Slice 2–3: **Person** | Documented in vs-README + this plan | ✅ **Done** — **Person** (simplest numeric property `Age`) |

**Out of scope:** policies, relationships, effects execution.

**Done when:** S1.1–S1.2 closed.

**Status:** ✅ **Done** — Person pinned; coverage verified.

**Micro-tasks:** [`vs-s1-verify-structure-path.md`](simple-agent-tasks/vs-s1-verify-structure-path.md) ✅ · [`vs-s1-pin-canonical-entity.md`](simple-agent-tasks/vs-s1-pin-canonical-entity.md) ✅

**Next:** Slice 2 — Policy runtime (direct API only)

---

### Slice 2 — Policy runtime (direct API only)

**Story:** Domain-attached policy evaluates true/false on a valid subject via **product** API.

```text
Domain with Entity + Property + Policy(DomainExpression)
  → PolicyEvaluator.Evaluate / CompileVMPredicate
  → bool on CLR record / approved subject helper
  → reject invalid subjects
```

| ID | Task | Exit check | Maps to |
|----|------|------------|---------|
| **S2.1** | Product subject helper defaults (non-null bags; no Dict/Expando) | Helper + tests I1–I3 | ws8 **6d**, **6h** |
| **S2.2** | Bool ABI adult assert (true is bool, not only `1L`) | Dual path assert | ws8 **6e** |
| **S2.3** | `MatchNumeric` (or Age≥N) positive control true/false | VM + dual-oracle | ws8 **6f** |
| **S2.4** | Property name alignment: domain property ↔ DE ↔ subject | Documented + test | ws8 **6g** |
| **S2.5** | Domain-attached policy e2e on **canonical entity** (Person/Order) via direct API only | One “definition of done” test file | PolicyVmEvaluationTests / new |
| **S2.6** | Optional: fail closed DiffDays / date if slice expressions need dates — else document “not in slice” | No silent wrong days | Review F if pulled |

**Depends on:** S0.3, S0.4 if expressions invoke instance methods.

**Out of scope:** MCP add/evaluate tools (Slice 3); free-form AST from agents.

**Done when:** S2.1–S2.5 green; agent-facing claim “policies evaluate on VM” is true **on direct API**.

**Status:** 🟡 Core tests exist; product enforcement + invariants incomplete.

---

### Slice 3 — Policy MCP product loop

**Story:** Agent attaches and evaluates a policy without core test hacks.

```text
MCP: create session → structure (Slice 1) → add_policy → get_policy_expression
  → evaluate_policy (sample subject JSON → VM bool)
  → true and false cases
```

| ID | Task | Exit check | Maps to |
|----|------|------------|---------|
| **S3.1** | Constrained expression contract for `add_policy` (no free-form AST bags) | Schema/docs + reject bad payload | ws8 **7a** |
| **S3.2** | `add_policy` tool (direct API only under the hood) | Policy appears on entity; analysis gate | ws8 **7** |
| **S3.3** | `evaluate_policy` tool — returns VM bool; never claims eval without result | Honesty invariant I4 | ws8 **8**, **11** |
| **S3.4** | MCP e2e smoke: structure + policy + eval true/false | One smoke class | ws8 **9** |
| **S3.5** | Polish: affordances, diagnostics, README for policy tools | Agent-usable | ws8 **10** |

**Depends on:** Slice 2 exit; S0.1–S0.2 for evolve honesty.

**Out of scope:** codegen, effect simulation, multi-policy engines.

**Done when:** S3.1–S3.5 green → **M2 product-complete** for first consumer happy path steps 1–6.

**Status:** ⬜ Not started (get_policy_expression exists; add/evaluate do not).

---

### ── Checkpoint: M2 closed ──

After Slice 3:

- Update `master-roadmap.md` / `v3-completion-plan.md`: M2 **Done** with vertical policy loop.  
- WP5 “runtime truth” treated **Done** for policy path.  
- Remaining work is **not** “finish V2→V3”; it is **product generation / T2** and **WP9 pull-only**.

---

### Slice 4 — First effect execution (optional, post-M2)

**Story:** One action effect runs for real (not only analyzed).

| ID | Task | Exit check |
|----|------|------------|
| **S4.0** | Choose **one** effect: `AssignEffect` *or* `StageTransitionEffect` | Written decision |
| **S4.1** | Spike: runtime subject model (CLR record mutation vs domain instance) | Spike note; no parallel VM |
| **S4.2** | Lower or apply one effect through generic ops / Interpreter | One e2e test |
| **S4.3** | Direct API helper + optional MCP later | Call site + test |

**Pull trigger:** First-customer product needs mutable behavior, not only guards.  
**Status:** ⬜ Deferred until Slice 3 done (unless a hard product need forces earlier).

---

### Slice 5 — Relationship (optional)

**Story:** Second entity + relationship authorable and queryable; only if product needs it.

| ID | Task | Exit check |
|----|------|------------|
| **S5.1** | Direct API: two entities + relationship; analysis clean | Test |
| **S5.2** | MCP already has `add_relationship` — smoke with second entity | Smoke |
| **S5.3** | DE relationship navigation only if policy needs it | Lower + VM or fail closed |

**Pull trigger:** Canonical slice needs link (e.g. Order→Customer).  
**Status:** ⬜ Pull-only.

---

## 4. Explicitly out of this finish plan

| Item | Where it lives |
|------|----------------|
| Actor / claims / UAC | WP9 |
| Full effect catalog execution | After Slice 4 pattern proven |
| Contract/interface codegen | WP9 |
| Validation module revive | Review WP-H keep/kill |
| Poly.Text / orphan cleanup | Review WP-H (do not block slices) |
| Multi-host Introspection | Trust ADR deferred |
| T2 product domain + generated modules | Trust ADR — **after** M2 + generation loop |
| DSL export/import | Deferred (first-v3-consumer) |
| Fail-closed all VM POC nodes | Review WP-E — pull when a slice hits them |

---

## 5. Suggested calendar (agent / human)

| Order | Focus | Parallel OK? |
|-------|--------|--------------|
| 1 | **Slice 1.2** pin canonical entity (Person or Order) | **Active next** |
| 2 | **Slice 2** policy runtime (direct API) | After 1.2 |
| 3 | **Slice 3** policy MCP loop | After Slice 2 |
| 4 | Declare M2 complete | After Slice 3 |
| — | Optional: 0.1d remove-zero-match; 0.2a README nit | Anytime |

**Simple agents:** execute only from [`simple-agent-tasks/vs-README.md`](simple-agent-tasks/vs-README.md) (`vs-s0-*` … `vs-s3-*`). Older `ws8-*` files are optional reference; **this document owns slice exit criteria**.

---

## 6. Tracking table

| Slice | Status | Notes |
|-------|--------|-------|
| 0 Honesty | ✅ **Done** | Optional polish: 0.1d, 0.2a |
| 1 Structure | ✅ **Done** | Coverage verified; **Person** pinned as canonical |
| 2 Policy API | 🟡 **Active** | Start vs-s2-* tasks |
| 3 Policy MCP | ⬜ | M2 close |
| 4 First effect | ⬜ Deferred | |
| 5 Relationship | ⬜ Pull | |

Update this table when a slice exits.

---

## 7. One-line recap

> **V2 is dead. Finish V3 by shipping one honest vertical path at a time: honesty → structure → policy on direct API → policy on MCP → then optional effects/links — never breadth before the active slice is fully green.**
