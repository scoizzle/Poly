# DomainModeling — abstraction path (for Scot)

**Date:** 2026-09-21
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite. Do not change `simple-agent-tasks/PIPELINE-STATUS.md`. Eng WIP stays **0** until Scot greenlights a slice.
**Grounding:** [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) @ `bc176c49` · [`domain-modeling-salvage-verdict-2026-09-18.md`](domain-modeling-salvage-verdict-2026-09-18.md) (middle path = **refactor behind abstraction**, not keep/kill as mill queue)
**Parked (locked):** store-vs-lower **Item 5** Occupancy / `BusySections`. Still PARKED. Do not unpark.
**Audience:** Scot

This note does not implement C#, does not delete code, and does not start a slice.

**Path (unlocked):** not hard reset / wipe · not salvage-in-place of the mess · **yes** simplify DomainModeling by refactoring behind the abstraction below.

---

## 1) Target abstraction

**Name: Session Compile**

A domain file states **facts** and names libraries with `uses` ids. **DomainSession** is the only compile unit: it loads those libraries (analyzers, type maps, artifact producers), runs **one** analyze (bags on nodes), and **fail-closes** if analyze is dirty. After a clean analyze it does two things on that same result: **`session.Lower`** emits **one** operation module (generic Syntax; the tree has no bags), and library **artifact producers** emit host files from **surface bags** that **call** that module. Store, DEI, `Stay.Create`, MCP, and C# print are **consumers** of that module (or of surface bags for host bind). None of them are the domain, and none invent a second meaning path.

---

## 2) What collapses because of it

Only what Session Compile honestly makes redundant — not a folder kill list:

| Collapses | Why |
|-----------|-----|
| **Dual trees** (`UseThisReference` / runtime-shaped vs emit `this`) | One `session.Lower` module; simulate and print bind the same tree. |
| **DEI / green harness as proof** | Scratch bind is a consumer. Product proof is the module (VM or generated types). |
| **`Stay.Create` / `CreateNav` as create meaning** | Host bind of a Create job the module already names — not operation meaning. |
| **Execute-time `LowerActionBody` residual** | Nested transition flush / Domain-null standalone belong in `session.Lower`, not execute input. |
| **Freestyle library→library deps** | Libraries may stack; dependents of the stack depend on **core**. No sideways mesh. |
| **Ad-hoc host pipelines** | Producers register at Load, run only after clean analyze. No compiler-mode invent of `Program.cs`, no `Domain.ResolveHost` third assembler. |
| **Consumer lowering flags** | Fix lowering once. Do not grow a sibling flag to keep one consumer green. |

Does **not** collapse (and this note does not delete): Ontology facts, `.poly`, session/libraries, evolution, fact-publishing analysis, scratch DEI as *current* bind, Item 5.

---

## 3) Ordered refactor slices (Scot approve before eng)

Small, stop-conditioned. **No slice starts until Scot greenlights that slice.** Eng WIP = 0 until then.

| # | Slice | Stop condition | Explicitly out |
|---|-------|----------------|----------------|
| **A** | **One module body** — collapse `UseThisReference` sibling so simulate and print share one cached `session.Lower` tree | For a shipped op, VM execute and C# print of that body agree without a consumer flag | DEI delete, Item 5, CURRENT |
| **B** | **Lower at Lower** — move residual execute-time `LowerActionBody` (nested StageTransition flush / Domain-null standalone) into `session.Lower` | Execute never lowers; authoring IR is parse output only | Salvage Runtime as product, MCP theater |
| **C** | **One producer loop** — host files only from libraries registered at Load, gated by clean analyze | No ad-hoc core/`ResolveHost` invent of host entry; producers fail closed on missing bags | Spec cycles/DAG/nested `uses` (still thin) |
| **D** | **Stack bottoms at core** — inventory + honesty: dependents of a composition name core, not inner libs as extras | No new freestyle mesh edges; unknown/duplicate `uses` still fail closed | Mill library-graph formalisms from this note |
| **E** | **Consumers labeled** — DEI / Store / `Stay.Create` documented and tested as bind, not meaning; product-surface tests call module or generated types | Harness smoke labeled harness; no new “DEI matches export” mill as proof | Delete DEI; mill `Stay.Create` removal before an honest Store bind |

**Between slices:** stop. Re-ask Scot. Do not chain A→E as one PR.

---

## Locks (repeat so agents do not “find” work)

- **Not CURRENT.** PIPELINE-STATUS stays `(none)`.
- **Item 5 PARKED.** Occupancy / `BusySections` — not a candidate.
- **No implement / no delete** from this file.
- **No wipe, no salvage-in-place.** Refactor behind Session Compile only.
- **PR 72** (library seams) independent — do not block or rebase this note onto it.

---

## Related (do not execute from here)

| Doc | Role |
|-----|------|
| [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) | Pipeline + principles this path sits on |
| [`domain-modeling-salvage-verdict-2026-09-18.md`](domain-modeling-salvage-verdict-2026-09-18.md) | Scot middle path = abstraction refactor (not old keep/kill queue) |
| `simple-agent-tasks/PIPELINE-STATUS.md` | Sole CURRENT/DONE — leave it |
