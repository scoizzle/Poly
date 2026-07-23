# Query + Effect Suite (post-E1) — Simple Agents

**Parents:**  
- [`../dsl-query-surface.md`](../dsl-query-surface.md) — **§3.1 + §4.0 frozen surface**; Q0 → Q1′  
- [`../effect-surface-completeness.md`](../effect-surface-completeness.md) — E2.1 (decision only in this suite)

**Date:** 2026-07-18  
**Baseline:** E1 delete-self shipped (`121cd92`); suite **1360** at suite open.  
**Audience:** One micro-task at a time; tiny reading list per file.

---

## Frozen product direction (read before any Q* task)

| Rule | Product form |
|------|----------------|
| **Anti-dot** | No `rel.Prop`, no `rel->Prop` |
| **Subject-first path-prefix** | `assignee Active`, `customer Tier is "VIP"` |
| **Postfix exists** | `assignee exists` |
| **Absence** | `not assignee exists` only (not `assignee not exists`, not prefix `exists assignee`) |
| **`where`** | Scope keyword; **no forced parens**: `customer where Status is "Active" and Tier is "VIP"` |
| **Quantifiers (Q3′)** | `any`/`all`/`none`/`count` **Rel where …** |
| **Cross-entity reads** | **Legal** — policies + **scalar** assign RHS (`assign Label to customer Tier`) |
| **Cross-entity writes** | **Banned** — assign target is **this** entity only |

Full design: parent plan §3.1 + §4.0. **Do not re-open dots or C# LINQ method chains.**

---

## Why this suite

Lifecycle **effects** are green enough for Order/Customer dogfood.  
Customer **policies** still cannot **read** related data in product DSL — IR + lowering already can.  
**Link** still needs a written E2.1 decision (graph **writes** stay explicit).

**Usefulness bar:** kernel effects + **Q1′** (path-prefix + `Rel exists` + to-one `where`) + honest non-goals. Not full LINQ. Not multi-entity invoke yet.

---

## Operating rules (mandatory)

1. **One task at a time.** Claim it (Status → `[~]`) before coding.
2. **Pick the first `[ ]` in the ordered table below.** Do not skip unless the row says **parallel OK**.
3. **Do not start Slice Q1′** until **Slice Q0** exit is met (all required Q0 tasks `[x]`).
4. **E2.1** may run **after Q0.1–Q0.2** (parallel OK with Q0.3–Q0.5) — decision-only, no link DSL.
5. **Do not open Q3′ / E3b / E5 / L\*** unless an orchestrator reopens them.
6. After Done: write `../agent-summaries/qe-<task-id>-summary.md` using [`TEMPLATE-task-summary.md`](../agent-summaries/TEMPLATE-task-summary.md). Update only the Status line on the task file.
7. Build: `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`  
   Tests: `dotnet run --project Poly.Tests/Poly.Tests.csproj` (filter when possible).
8. Principles: AGENTS.md — domain fidelity, thin slice, guide honesty same PR as surface change, rebuild `get_dsl_guide` embed after guide edits.
9. **Shipped only after commit** — do not mark parent plans “shipped” while the tree is dirty.
10. **Assign:** never accept related **LHS**; scalar related **RHS** is a cross-entity **read** (legal when path-prefix ships).
11. **Pre-ship review gate:** Before marking any slice `[x]`, execute [`pr1-uncommitted-review-gate.md`](pr1-uncommitted-review-gate.md) — review dirty files, categorize findings (🔴🟠🟡⚪), harden with three-layer defense + fail-closed posture, re-review, and only ship when clean. The review gate is not a separate task to claim — it is the **last step before `[x]` on every slice**.

### Status marks

| Mark | Meaning |
|------|---------|
| `[ ]` | Not Started — pickable when prerequisites Done |
| `[~]` | In Progress |
| `[x]` | Done |
| **Skip** | Do not execute (deferred / pull) |

---

## Pick order

### Slice Q0 — Expression honesty + freeze (**required first**)

| # | Task | File | Status | Notes |
|---|------|------|--------|-------|
| **Q0.1** | Document **shipped** DSL expression grammar in guide | [`qe-q0-1-guide-expression-grammar.md`](qe-q0-1-guide-expression-grammar.md) | `[x]` | Local and/or/not/compare only — no overclaim |
| **Q0.2** | Document IR gaps + **planned** subject-first surface | [`qe-q0-2-guide-ir-expression-gaps.md`](qe-q0-2-guide-ir-expression-gaps.md) | `[x]` | Planned ≠ shipped |
| **Q0.3** | Expression matrix: DE × DSL × JSON × lower × VM | [`qe-q0-3-expression-parity-matrix.md`](qe-q0-3-expression-parity-matrix.md) | `[x]` | Mark Q1′ / Q3′ columns |
| **Q0.4** | Confirm Q3′ keyword set (`any`/`all`/`none`/`count` + `where`) | [`qe-q0-4-method-syntax-keywords.md`](qe-q0-4-method-syntax-keywords.md) | `[x]` | Keyword form, not C# methods |
| **Q0.5** | Customer must-have list (product spellings) | [`qe-q0-5-customer-must-have.md`](qe-q0-5-customer-must-have.md) | `[x]` | Ticket/Order sentences |

**Slice Q0 exit:** Guide honest about shipped; planned dialect named; matrix exists; Q1′ vs Q3′ clear.

### Slice E — Link decision (**after Q0.1–Q0.2; parallel OK with rest of Q0**)

| # | Task | File | Status | Notes |
|---|------|------|--------|-------|
| **E2.1** | Record create-in-only vs bag/param link decision | [`qe-e21-link-product-decision.md`](qe-e21-link-product-decision.md) | `[x]` | Graph **writes**; no link DSL |

**Default if unsure:** **(a) create-in only** — aligns with “cross-entity writes banned” via assign.

### Slice Q1′ — Path-prefix + postfix exists + to-one `where` (**after Q0 exit**)

| # | Task | File | Status | Notes |
|---|------|------|--------|-------|
| **Q1.1** | Spec residual: BNF, where-body, many-exists | [`qe-q1-1-nav-exists-syntax-spec.md`](qe-q1-1-nav-exists-syntax-spec.md) | `[x]` | Direction frozen; fill open bits only |
| **Q1.2** | Parse/print/lower **path-prefix** (+ scalar assign RHS) | [`qe-q1-2-parse-nav-owned.md`](qe-q1-2-parse-nav-owned.md) | `[x]` | `Rel Prop`, `Rel Prop op value` |
| **Q1.3** | Parse/print/lower **`Rel exists`** / **`not Rel exists`** | [`qe-q1-3-parse-exists.md`](qe-q1-3-parse-exists.md) | `[x]` | Postfix only |
| **Q1.3b** | Parse/print/lower to-one **`Rel where` and-chain** | [`qe-q1-3b-parse-where-rebind.md`](qe-q1-3b-parse-where-rebind.md) | `[x]` | No forced parens |
| **Q1.4** | Goldens: policies + assign RHS read + reject related LHS | [`qe-q1-4-nav-exists-goldens.md`](qe-q1-4-nav-exists-goldens.md) | `[x]` | evaluate / simulate / require |
| **Q1.5** | JSON policy parity or documented split | [`qe-q1-5-json-policy-parity.md`](qe-q1-5-json-policy-parity.md) | `[x]` | Prefer document split if thin |
| **Q1.6** | Guide examples + §3.1 read/write rule | [`qe-q1-6-guide-nav-exists-examples.md`](qe-q1-6-guide-nav-exists-examples.md) | `[x]` | Subject-first only |

**Slice Q1′ exit (parse/print):** met (`959c6e7`).  
**Q1′′′ partial (`3c99221`):** assign + owned print + apply/export.  
**Q1′′′′ (`76568a3`):** **DMREL001**; authoring-only tests.  
**Q1''''' (`514e21c`):** guide; nested-where; unknown-rel; body→target.  
**Q1'''''' (`25a79ec`):** **Rel exists on N1 nav** fixed + goldens.  
**Q3′ core (`bb5032b`):** **any/all/none/count** DSL + analysis + store-aware `EvaluatePolicy` + guide — **shipped** (not “from scratch”).  
**Current:** **§17 Q3.R′** honesty gate on uncommitted residual batch — **do not invent `link_instances`**.

### Slice Q3′ — Collection quantifiers

**Product vertical already in tree** (do not re-implement IR/parser from zero).  
Thin “2B” route = **close residuals** with simple-agent tasks, not rewrite Q3′.

| # | Task | File | Status | Difficulty |
|---|------|------|--------|------------|
| **Q3.R0** | Inventory shipped Q3′ (read-only) | [`qe-q3-r0-status-inventory.md`](qe-q3-r0-status-inventory.md) | `[x]` code | Small |
| **Q3.R1** | Guide honesty (shipped vs not; eval claim) | [`qe-q3-r1-guide-honesty.md`](qe-q3-r1-guide-honesty.md) | `[~]` reopen — invent `link_instances` | Small |
| **Q3.R2** | MCP `evaluate_policy` true/false for `any` | [`qe-q3-r2-mcp-evaluate-any-golden.md`](qe-q3-r2-mcp-evaluate-any-golden.md) | `[~]` true/false green; Description honesty open | Medium |
| **Q3.R3** | Empty-collection semantics in guide | [`qe-q3-r3-empty-semantics-docs.md`](qe-q3-r3-empty-semantics-docs.md) | `[x]` code | Small |
| **Q3.R4** | Plan/roadmap status sync | [`qe-q3-r4-plan-sync.md`](qe-q3-r4-plan-sync.md) | `[~]` after §17 | Small |
| **Q3.R′** | Honesty gate from review | parent **[§17](../dsl-query-surface.md)** | `[ ]` | Small |
| **Gate** | [`pr1-uncommitted-review-gate.md`](pr1-uncommitted-review-gate.md) before shipping residual batch | — | `[ ]` | Process |

**Pick order:** **Q3.R′.1–.2 first** (Description + guide — no invent `link_instances`) → re-gate → commit.  
**Exit:** Guide + Description honest; MCP any e2e green; plans say Q3′ shipped + residuals closed **only after commit**.

### Optional hygiene (parallel anytime; do not block)

| # | Task | File | Status | Notes |
|---|------|------|--------|-------|
| **Q1'''''''.1** | Guide: nested path-prefix in where body | (inline if small) | `[ ]` | Low |
| **E1′′′.1** | Guide: `delete` reserved keyword | [`qe-opt-e1-reserved-delete.md`](qe-opt-e1-reserved-delete.md) | `[ ]` | Low |
| **E1′′′.3** | Fail-loud bad effect token error-string smoke | [`qe-opt-e1-bad-effect-token-test.md`](qe-opt-e1-bad-effect-token-test.md) | `[ ]` | Low |

### Explicitly not in this suite (pull / later)

| Item | Plan | Why |
|------|------|-----|
| Re-implement Q3′ IR/parser | — | **Already shipped** `bb5032b` |
| E2.2–E2.5 link implement | effect-surface | Only if E2.1 reopens |
| E3b multi-entity invoke | effect-surface | Peer **writes** |
| Q4 aggregates sum/min/max | query-surface | Pull |
| E5 micro effect tools | effect-surface | Dogfood quotes only |
| L\* C#/MSIL/containers | phase3 §6d | Post–P3 |
| Product dots / C# LINQ chains | — | Rejected |

---

## Suggested agent sessions

| Session | Tasks | Outcome |
|---------|-------|---------|
| **Q3 residual — Honesty** | Q3.R0 → Q3.R1 → Q3.R3 | Guide + empty semantics (partial — reopen R1) |
| **Q3 residual — Prove MCP** | Q3.R2 | evaluate_policy any true/false (`instanceId`) |
| **Q3 residual — §17 gate** | Q3.R′.1–.2 → Gate | Remove invent `link_instances`; dual-path guide; re-review |
| **Q3 residual — Plans** | Q3.R4 after gate | Roadmap/pick sync + commit |
| **Optional** | E1′′′.1 / .3 | Hygiene |

---

## Parent plan updates (orchestrator after slice exits)

| When | Update |
|------|--------|
| Q0 exit | `dsl-query-surface.md` Q0 checklist + agent pick → Q1′ |
| E2.1 done | `effect-surface-completeness.md` decision log + E2.1 checkbox |
| Q1′ exit | query success criteria; expansion §0; master-roadmap row 8 |
| Any guide edit | Rebuild MCP so `get_dsl_guide` embeds new content |

---

## Canonical reading (suite-wide)

- `Agents.md` principles 1–5  
- `docs/CORE.md` — DomainModeling ownership  
- **Parent plan §3.1 + §4.0** (frozen surface)  
- Product guide: `Poly.Mcp/Docs/poly-dsl-agent-guide.md`  
- IR: `Poly/DomainModeling/DomainExpression.cs`  
- Lower: `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs`  
- Parser/printer: `Poly/DomainModeling/Parsing/PolyDslParser.cs`, `DomainDslPrinter.cs`
