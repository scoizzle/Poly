# DomainModeling — abstraction path (for Scot)

**Date:** 2026-09-21
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite. Do not change `simple-agent-tasks/PIPELINE-STATUS.md`. Eng WIP stays **0** until Scot greenlights a slice.
**Grounding:** [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) on this branch (principles SHA moved with talk alignment + this sim-rule amend) · [`domain-modeling-salvage-verdict-2026-09-18.md`](domain-modeling-salvage-verdict-2026-09-18.md) (middle path = **refactor behind abstraction**, not keep/kill as mill queue)
**Parked (locked):** store-vs-lower **Item 5** Occupancy / `BusySections`. Still PARKED. Do not unpark.
**Audience:** Scot

This note does not implement C#, does not delete code, and does not start a slice.

**Path (unlocked):** not hard reset / wipe · not salvage-in-place of the mess · **yes** simplify DomainModeling by refactoring behind the abstraction below.

---

## 1) Target abstraction

**Name: Session Compile**

A domain file states **facts** and names libraries with `uses` ids. **DomainSession** is the only compile unit: it loads those libraries (analyzers, type maps, artifact producers), runs **one** analyze (bags on nodes), and **fail-closes** if analyze is dirty. After a clean analyze it does two things on that same result: **`session.Lower`** emits **one** operation module (generic Syntax; the tree has no bags), and library **artifact producers** emit host files from **surface bags** that **call** that module (host call-site delivery — **not** simulate). **Simulation** = **Interpreter** executing that real lowered tree. Store, DEI, `Stay.Create`, MCP, and C# print are **consumers**. None of them are the domain. DEI / Effect-IR / fake runtime walks are **not** product sim.

**Product rule (Scot):** drop everything that fakes a runtime for simulation. Facts → bags → `session.Lower` (one module) → Interpreter runs that module. Producers deliver host call-sites. Not CURRENT. No slice greenlight.

**Lower plan (next, before eng):** [`session-lower-plan-2026-09-21.md`](session-lower-plan-2026-09-21.md). Slices A–E stay unapproved until Scot accepts that plan.

---

## 1a) Talk alignment (Alexandrescu + Coyle)

Same two talks as the principles note. They name **Session Compile**. They do **not** greenlight slices A–E.

**Andrei Alexandrescu**, ACCU ([video](https://youtu.be/-RWdevA0gWI)): abstraction is the centerpiece. It **compresses context** for humans and AI. The **high-level structure must persist** through a “just fix.” Specs can be **variable-precision**. Avoid the **reverse-centaur**: the human as rubber-stamp for opaque agent output.

**Frank Coyle** ([video](https://www.youtube.com/watch?v=Sir59K8ZDPU)): **agents need ontologies** — formal shared conceptualization, inference, constraints. Neural agents + **symbolic guardrails**. **Validate before side effects.** **Pydantic-at-door / ontology-at-ledger.**

| Talk idea | Session Compile (this path) |
|-----------|-----------------------------|
| The one picture (**context compression**) | Session Compile: facts → bags → `session.Lower` (**one module**) → **Interpreter** executes it; library **artifact producers** = host call-sites, not sim |
| **High-level structure that must persist** | That compile unit + Interpreter on that tree. Collapse table: not dual trees, not DEI-as-sim, not ad-hoc host pipelines |
| **Reverse-centaur** | Do not “prove” meaning via harness / MCP / DEI while the module is wrong. Product sim is Interpreter on the real tree |
| **Ontology-at-ledger** | Domain facts + bags + lowered module |
| **Pydantic-at-door** | Surface bags / host producers / MCP tool shapes — validate at the door; meaning stays in the ledger |
| **Validate before side effects** | **Fail-closed analyze**: dirty → STOP (no Lower, no host files, no execute invent) |
| Domain is not the consumers | Library of legal operations; Store / DEI / MCP / print are not meaning. DEI is not product sim |

Slices below stay **unapproved**. Scot greenlight required. **Not CURRENT.** Item 5 PARKED. Eng WIP = 0.

---

## 2) What collapses because of it

Only what Session Compile honestly makes redundant — not a folder kill list:

| Collapses | Why |
|-----------|-----|
| **Dual trees** (`UseThisReference` / runtime-shaped vs emit `this`) | One `session.Lower` module; **Interpreter** and print bind the same tree. |
| **DEI / Effect-IR / fake runtime as simulate** | Not product sim. Scratch bind is harness debt. Product sim is Interpreter on the module. |
| **`Stay.Create` / `CreateNav` as create meaning** | Host bind of a Create job the module already names — call-site, not operation meaning, not sim. |
| **Execute-time `LowerActionBody` residual** | Nested transition flush / Domain-null standalone belong in `session.Lower`, not execute input. |
| **Freestyle library→library deps** | Libraries may stack; dependents of the stack depend on **core**. No sideways mesh. |
| **Ad-hoc host pipelines** | Producers register at Load, run only after clean analyze. Host call-sites, not a fake simulate path. No compiler-mode invent of `Program.cs`, no `Domain.ResolveHost` third assembler. |
| **Consumer lowering flags** | Fix lowering once. Do not grow a sibling flag to keep one consumer green. |

Does **not** collapse (and this note does not delete): Ontology facts, `.poly`, session/libraries, evolution, fact-publishing analysis, scratch DEI as *current harness bind* (not sim), Item 5.

---

## 3) Ordered refactor slices (Scot approve before eng)

Small, stop-conditioned. **No slice starts until Scot accepts the Lower plan, then greenlights that slice.** Eng WIP = 0 until then.

**Order (not a greenlight):** **A** then **B** are **first** — they make “one real tree” true (Interpreter can run what Lower emitted; execute never invents a second tree). **E** is the honesty slice: **DEI is not a simulate path**. C and D stay as listed. Still unapproved. Still not CURRENT.

| # | Slice | Stop condition | Explicitly out |
|---|-------|----------------|----------------|
| **A** | **One module body** (**first**) — collapse `UseThisReference` sibling so **Interpreter** and print share one cached `session.Lower` tree | For a shipped op, Interpreter execute and C# print of that body agree without a consumer flag | DEI delete, Item 5, CURRENT |
| **B** | **Lower at Lower** (**first**, with A) — move residual execute-time `LowerActionBody` (nested StageTransition flush / Domain-null standalone) into `session.Lower` | Execute never lowers; Interpreter only runs the Lowered module; authoring IR is parse output only | Salvage Runtime as product, MCP theater |
| **C** | **One producer loop** — host **call-sites** only from libraries registered at Load, gated by clean analyze. Producers are delivery, not simulate. | No ad-hoc core/`ResolveHost` invent of host entry; producers fail closed on missing bags | Spec cycles/DAG/nested `uses` (still thin) |
| **D** | **Stack bottoms at core** — inventory + honesty: dependents of a composition name core, not inner libs as extras | No new freestyle mesh edges; unknown/duplicate `uses` still fail closed | Mill library-graph formalisms from this note |
| **E** | **DEI not a simulate path** — DEI / harness bind must not pretend to be simulation or proof; product sim is **Interpreter** on the module | Harness smoke labeled harness; no DEI / Effect-IR walk as sim; product-surface tests call Interpreter or generated types | Delete DEI; mill `Stay.Create` removal before an honest Store bind |

**Between slices:** stop. Re-ask Scot. Do not chain A→E as one PR.

---

## Locks (repeat so agents do not “find” work)

- **Not CURRENT.** PIPELINE-STATUS stays `(none)`.
- **Item 5 PARKED.** Occupancy / `BusySections` — not a candidate.
- **Product sim = Interpreter on the `session.Lower` tree.** DEI / Effect-IR are not sim. This file does not delete DEI.
- **No implement / no delete** from this file.
- **No wipe, no salvage-in-place.** Refactor behind Session Compile only.
- **PR 72** (library seams) independent — do not block or rebase this note onto it.

---

## Related (do not execute from here)

| Doc | Role |
|-----|------|
| [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) | Pipeline + principles this path sits on |
| [`session-lower-plan-2026-09-21.md`](session-lower-plan-2026-09-21.md) | Refine `session.Lower` before eng. Slices unapproved until Scot accepts. |
| [`domain-modeling-salvage-verdict-2026-09-18.md`](domain-modeling-salvage-verdict-2026-09-18.md) | Scot middle path = abstraction refactor (not old keep/kill queue) |
| `simple-agent-tasks/PIPELINE-STATUS.md` | Sole CURRENT/DONE — leave it |
