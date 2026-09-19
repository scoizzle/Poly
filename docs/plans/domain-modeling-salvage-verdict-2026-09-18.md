# DomainModeling salvage verdict (past node definitions)

**Date:** 2026-09-18
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite. Do not change [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md).
**SHA:** `934b2409` (merge of [PR 71](https://github.com/scoizzle/Poly/pull/71); tip includes PR 68–71 analysis FailFast / snapshot / `HasErrors`)
**Open PR out of scope:** [PR 72](https://github.com/scoizzle/Poly/pull/72) `refactor/domainmodeling-core-library-seams` — library extract (Temporal out of the core list, registration-order schedule). **Do not block. Do not review. Do not rebase this note onto it.**
**Parked (locked):** store-vs-lower **Item 5** Occupancy / `BusySections`. Still PARKED. Not a candidate in this verdict.
**North star:** `session.Lower` is domain meaning. DomainModeling past Node is a **front-end compiler** (facts → bags → one operation module), not a second interpreter and not a process.
**Hard lines:** [`docs/CORE.md`](../CORE.md) §0 / hard lines · [`docs/decisions/2026-09-04-frozen-core-pipeline.md`](../decisions/2026-09-04-frozen-core-pipeline.md) · [`docs/decisions/2026-09-05-lowered-module-is-domain-meaning.md`](../decisions/2026-09-05-lowered-module-is-domain-meaning.md) · [`docs/decisions/2026-09-03-facts-concerns-bags-store-bind.md`](../decisions/2026-09-03-facts-concerns-bags-store-bind.md)

This note does not implement C#, does not delete code, does not change PIPELINE-STATUS, and is not a CURRENT suite.

Grounding copies (historical, **stale vs this tip** — do not treat as live queue): [`_grounding_read/store-vs-lower-ranking-2026-09-08.md`](_grounding_read/store-vs-lower-ranking-2026-09-08.md) · [`_grounding_read/analysis-leftover-inventory-2026-09-11.md`](_grounding_read/analysis-leftover-inventory-2026-09-11.md). Those files are checkout-only; they are **not** part of this commit.

---

## One-line verdict

**Partial cut** — keep Ontology facts, `.poly`, session/libraries, evolution, and lower-to-Node; stop treating Runtime/DEI, `Stay.Create`, and residual execute-time Effect IR as DomainModeling; do not hard-reset the authoring IR.

---

## What “past node definitions” means

Frozen core already owns **AST / Node / Analysis**. DomainModeling past that freeze is everything that is *not* a Syntax node: `Domain` facts, `DomainExpression` / `Effect` authoring IR, `.poly`, session load, domain analysis bags, lower-to-Node, scratch bind, C# print, Store jobs.

The question is not “are Nodes the primary?” (they are). The question is whether that front-end should be **salvaged in place**, **cut back to the compiler spine**, or **thrown away and rebuilt as Nodes**.

Tip size (order of magnitude, not a budget): ~26k lines under `Poly/DomainModeling/` (159 `.cs` files). Ontology records are the cheap part (~1k at the folder root, plus Constraints / Effects / Contract). The cost sits in Analysis (~8.7k), Lowering (~4.6k), Runtime (~3.5k), Language (~3.4k).

---

## Why not salvage, why not hard reset

**Not salvage.** Salvage would mill DomainModeling as one blob: DEI dual-path, Unique-only `RestoreActionState`, `Stay.Create` factories, lint-only analysis, exporter-as-stage-3, occupancy dogfood. That is the attractor [ADR 2026-09-05](../decisions/2026-09-05-lowered-module-is-domain-meaning.md) forbids — growing harness so fake implementation is cheaper than finishing lowering. CORE §0 already names scratch `DomainEntityInstance` / MCP walks as **not** product-surface proof.

**Not hard reset.** Hard reset would delete or bypass the authoring IR and author Syntax Nodes (or a new facts layer) from scratch. That burns the cheap correct part: `Domain` = facts (`uses` ids), `.poly`, `DomainSession`, evolution, `session.Lower`. It also burns [PR 72](https://github.com/scoizzle/Poly/pull/72)’s library extract on the existing session seam. Frozen CORE forbids a parallel product IR and forbids inventing `Main`. The 2026-08-14 deletion-first note already said the model records are not the problem; three unfinished refactors on one spine were. Two of those (pack nouns, analysis sequential leftovers) have since been cut. The remaining unfinished refactor is **runtime vs export as two trees**, not “Ontology is wrong.”

**Partial cut** is the only option that keeps DomainModeling as the front-end compiler and stops spending IC on current consumers as if they were the architecture.

---

## Ranked options

Rank is **where to spend (or stop spending) IC**, not chronology. Keep/kill in this table is **intent**. Nothing in this note deletes. Kill rows are “do not mill as product; cut when a later slice is admitted.”

| Rank | Option | Keep | Kill (later; not this note) | Stop |
|------|--------|------|------------------------------|------|
| **1 (pick)** | **Partial cut** | Ontology facts (`Domain`, Entity, Stage, Action, Policy, Constraint, Effect records, `DomainExpression`, `ImportedContract`). `Language/` (`.poly` parse/print). `Compile/` (`DomainSession`, `ExtensionCatalog`, `IDomainLibrary`). `Evolution/`. Lowering **passes** + `session.Lower` / `DomainProgramProjection.ToSyntax` as the stage-3 door. Fact-publishing analysis (catalog, capability, runtime-contract dispatch plan, required properties, effect facts, entity structure, topology, ownership, storage). `Libraries/` as session load (Temporal / Storage). `ContractFill/` (another Domain → `ImportedContract`). `Meaning/` session tables (folds, forms, type maps). `Dispatch/` as **closed-world route of parse output**, not execute. | Execute-time `LowerActionBody` residual (nested StageTransition flush + Domain-null standalone) as shipped execute input. `UseThisReference` sibling trees in `RuntimeAnalysisCache` as a second module. Unique-only `RestoreActionState` as product atomicity. `Stay.Create` / `CreateNav` as shipped create meaning. DEI / MCP walks as T2 proof. Occupancy / `BusySections` keywords. Lint-only analysis as a DomainModeling rewrite. `DomainModelAnalyzer.Analyze` as the product door. | Authoring IR is parse output only. Domain-bound named action / policy / sub / entry-exit bind cached module bodies. Simulate and print of **one** `session.Lower` tree agree for shipped ops. Scratch bind and C# factories remain **current consumers**, documented as such. PR 72 merges or parks on its own. Item 5 stays PARKED. No CURRENT admission. |
| **2 (do not pick)** | **Salvage in place** | Everything in rank 1 **plus** mill Runtime/DEI, exporter hot-wire, lint-only names, `Stay.Create` goldens, Hotel occupancy in DEI until they “match.” | Nothing until each leftover is locally green. | Dual-path is gone *and* DEI is still the oracle. That stop is unreachable without violating ADR 2026-09-05 (green DEI ≠ product proof). |
| **3 (do not pick)** | **Hard reset** | `Poly.Ast` / `Poly.Analysis` / Interpretation VM. A new thin facts list if someone re-derives `uses`. | `Poly/DomainModeling/**` as shipped meaning (Ontology, Language, session, evolution, lower, Runtime). | A second pipeline that authors Nodes (or a new IR) and still has to grow `.poly`, evolution, and library load. Forbidden growth: parallel product IR, `Main` in core, consumer lowering flags to keep C# print alive. |

### Folder keep / kill (rank 1, concrete)

| Folder / seam | Verdict | Notes |
|---------------|---------|-------|
| `Ontology/` | **KEEP** | Facts. Cheap. Frozen: `Domain` is facts, not Store, not HTTP. |
| `Language/` | **KEEP** | Product `.poly`. Guide stays in sync with parser/printer (not this note). |
| `Compile/` | **KEEP** | Session is the compile. Unknown / duplicate `uses` fail closed. PR 72 extracts Temporal from the core list **on this seam** — let it. |
| `Evolution/` | **KEEP** | Immutable apply + analysis gate. Mutation errors stay on `EvolutionResult` (PR 68). |
| `Lowering/` passes + `DomainProgramProjection` | **KEEP** | Stage 3. `EffectLoweringPass` product path is a real node, not `null`. |
| `Lowering/DomainToCSharpExporter*` | **KEEP as current print** | First print of the module, not architecture. Do not mill `Attach*` / `Stay.Create` as operation meaning. Stage-3 *owner* is still this exporter (PR 51 alignment); collapsing the façade is a later Lower slice, not a reset. |
| `Analysis/` fact emitters | **KEEP** | Catalog, capability, runtime contract, storage, entity structure, topology, ownership, effect facts, required properties. Bags on nodes. |
| `Analysis/` lint-only packs | **DEFER** | Structural, PolicyConstraint, EffectAnalyzer, ConstraintQuality, RuleCoverage, ContractIntegration, Subscription, AuthoringSuggestion, ExpressionType. Not this verdict’s mill. Different class from analysis-framework leftovers (PR 68–71). |
| `Analysis/RuntimeAnalysisCache` | **KEEP the cache; CUT the sibling tree** | `GetOrLower` populate is the product. `UseThisReference: false` VM-shaped copy vs emit `this` is residual dual-path (CORE §3.4 debt). Bind dictionary `This`; do not grow a third cache. |
| `Analysis/DomainModelAnalyzer` | **KEEP as pipeline factory; not the product door** | Product door is `DomainSession.Analyze`. Static `Analyze` is leftover compatibility. |
| `Libraries/` | **KEEP** | Session-loaded. PR 72’s job. Do not inline Temporal back into a core process list. |
| `Meaning/` | **KEEP** | Session tables. No process-wide `*.Default`. |
| `Dispatch/` | **KEEP as parse-output walk** | Closed-world `Effect` / `DomainExpression` route for lower/print/analyze. Must not become execute. |
| `Runtime/` (`DomainEntityInstance`, `DomainInstanceStore`) | **KEEP as scratch bind; not product proof** | Frozen ADR non-goal: do **not** delete DEI in this campaign. Harness tests stay labeled harness. |
| `Queries/` | **KEEP as harness projection** | MCP/overview. Presentation counts are multisets (analysis leftover rank 14). |
| `ContractFill/` | **KEEP** | Another Domain is `ImportedContract`, not a library id. |
| MCP (`Poly.Mcp/`) | **KEEP as harness** | Not DomainModeling; not the customer API. Do not grow `link_instances` the module does not name. |
| Occupancy / `BusySections` | **PARKED** | Item 5. Authoring pattern, not Store ABI, not a DomainModeling cut. |

---

## Tip evidence (why rank 1 is available)

Gone in C# (do not restore; do not mill as if live):

| Old dual | Tip |
|----------|-----|
| `LowerStageTransitions` | `rg` empty in `*.cs` |
| `ExecuteStructured` | comment only (`EffectLoweringPass`) |
| `EffectExecutor` | comment / test name only — no type |
| Analyzer DistinctBy / `ReportStructuralFailure` / concurrent diagnostics | PR 69 / 70 |
| FailFast vs Stop dual mode / live diagnostics alias / `HasErrors` dual | PR 71 (`ShouldStopOnErrors` is FailFast only) |

Still live residual (Lower / bind debt, **not** a reset of Ontology):

| Residual | Where | Class |
|----------|-------|-------|
| Nested StageTransition flush + Domain-null standalone still call `LowerActionBody` | `DomainEntityInstance.ExecuteEffectList` (`allowExecuteTimeLower`) | **LOWER** — CORE §3.4 still says subs/transition batches lower at execute time; that sentence is **partly doc-lag** (PR 63 caches Domain-bound hot path) and **partly true** for nested flush |
| `UseThisReference` emit vs VM-shaped sibling | `RuntimeAnalysisCache` + `LoweringContext` | **LOWER** — same consumer flag `LowerStageTransitions` was, now cached |
| `Stay.Create` / `CreateNav` | C# factories (`DomainToCSharpExporter.StoreBind`) | **STORE** bind of a job the module already names |
| Unique-only `RestoreActionState` | `DomainEntityInstance` invoke | **HARNESS** compensation — sequential fail-before-mutate is already a tree (PR 65); Unique restore is not product atomicity |
| `EvaluateDefaultValue` create-time `now`/`today`/`guid` | DEI `Create` bag fill | **LOWER leftover** on bag fill, not operation-tree clocks (P6 already put clocks in the tree) |
| Stage 3 call graph still `DomainToCSharpExporter` | `DomainProgramProjection.ToSyntax` | **LOWER** façade — door is named `session.Lower` |
| Lint-only domain passes | `DomainModelAnalyzer` pipeline comment | **DEFER** — domain-analysis simplification, not salvage |

Landed since the 2026-09-08 store-vs-lower copy (so agents do not reopen them from that SHA):

| Item in that ranking | Tip |
|----------------------|-----|
| Item 1 path-prefix require → Failure | PR 61 merged (already “done” there) |
| Item 3 subs / batches / policy at Lower | PR 63 merged; Domain-bound `EvaluatePolicy` binds cached policy bodies and **throws on miss** |
| Item 2 public Create inverse attach | **PR 62 merged** (was “in review” in the copy) |
| Item 4 sequential Failure as tree | **PR 65 merged** (PR 64 DEI restore mill was the wrong layer; Unique-only restore remains) |
| Item 6 constraint lower on assign | **PR 66 merged** (was “queued”) |
| Item 5 Occupancy / `BusySections` | **Still PARKED** |

Analysis leftovers (2026-09-11 copy) ranks 1–3 landed in PR 71. Remaining ranks there are docs ghosts, lookup-identity DEFER, or presentation — **not** a DomainModeling reset.

---

## PR 72 interaction

[PR 72](https://github.com/scoizzle/Poly/pull/72) `refactor/domainmodeling-core-library-seams` extracts libraries from the core pipeline (Temporal off the core list, registration-order schedule honesty).

| This note | PR 72 |
|-----------|-------|
| Does **not** review, merge-block, rebase, or rewrite files it touches | Independent library-load slice on `Compile/` + `Libraries/` + analyzer schedule |
| Rank 1 **wants** Temporal as a `uses` library, not a process-wide core list | That is PR 72’s direction |
| Rank 3 (hard reset) would discard the seam PR 72 is extracting | Another reason not to reset |
| If PR 72 merges, this verdict does not change | Re-read `ExtensionCatalog` / `DomainModelAnalyzer` registration only as CORE maintenance in that PR |
| If PR 72 parks, this verdict does not change | Do not inline Temporal by hand as “salvage” |

**Do not hold PR 72 for Occupancy, DEI deletion, `Stay.Create`, lint-only mill, or CURRENT admission.**

---

## What NOT to burn IC on / not delete yet

1. **Do not delete `DomainEntityInstance` / `DomainInstanceStore`.** Frozen ADR non-goal. Scratch bind stays until the module is the simulate+print oracle and a later slice replaces bind through frozen seams.
2. **Do not mill Occupancy / `BusySections` (Item 5).** PARKED. No keyword, no Store ABI, no Hotel DEI rewrite to match last-writer export. Hotel probe = harness smoke.
3. **Do not restore `EffectExecutor`, `ExecuteStructured`, `LowerStageTransitions`, analyzer DistinctBy, `ReportStructuralFailure`, concurrent diagnostic queues.** Gone. Comments that name them are tombstones.
4. **Do not mill Unique-only `RestoreActionState` as product atomicity.** Same stop as store-vs-lower item 4: fail-before-mutate lives in the tree (PR 65). Relabel restore as harness if it must move at all.
5. **Do not mill `Stay.Create` / `CreateNav` deletion** until an EF/SQLite Store binds the same Create job print already names. Factories are current consumers.
6. **Do not mill lint-only domain passes, StoragePass wrapper, catalog vs capability dual, Catalog `Last` vs Structural first** as this verdict. Analysis leftover ranks 5 / 15 / 18 stay DEFER. Needs a Scot reopen, not a salvage PR.
7. **Do not admit dict-sqlite, EF codegen, Stage 5c HTTP Domain walk, mut-safety, Grammar wrap-up, V3 naming** as this note’s work.
8. **Do not grow MCP theater** (`link_instances` the module does not name, new simulate tools, Hotel DEI as T2).
9. **Do not add a consumer-specific lowering flag** to keep C# print working. `UseThisReference` is already that flag; collapse it, do not add a sibling.
10. **Do not hard-reset Ontology / Language / session** because Runtime is still a god-object. Split or bind later; do not throw the compiler away.
11. **Do not block PR 72.**
12. **Do not admit this note as PIPELINE-STATUS CURRENT.** Proposal only.

---

## Explicit Scot decision asks

Landed unless Scot reopens (do not mill):

| Decision | Where |
|----------|-------|
| Nodes / Analysis / session libraries / one analyze → module + surface bags | Frozen ADR 2026-09-04 · CORE §0 |
| Lowered module is domain meaning; DEI is scratch bind, not T2 | ADR 2026-09-05 |
| Store jobs on the tree; uniqueness / graph wiring are Store, not factory theater | ADR 2026-09-03 · PR 62 Create attach |
| Sequential fail-before-mutate is a tree | PR 65 |
| Constraint-on-assign lowers | PR 66 |
| Analysis sequential + FailFast/snapshot/`HasErrors` | PR 68–71 |
| Item 5 Occupancy / `BusySections` PARKED | store-vs-lower ranking; **this note repeats the lock** |

Still a Scot call (this verdict does not mill them):

| Ask | Why it is a call | Recommended default if Scot does not want a meeting |
|-----|------------------|-----------------------------------------------------|
| **1. Confirm verdict = partial cut** (not salvage, not hard reset) | Rank 2 and 3 waste IC in opposite directions | **Partial cut** |
| **2. Confirm Item 5 stays PARKED** | Agents will “find” Hotel Occupied / `BusySections` as a DomainModeling gap | **Parked** |
| **3. Confirm PR 72 is independent** — no block, no “wait for salvage” | Library extract is the session seam rank 1 keeps | **Do not block** |
| **4. Next IC after PR 72, if any:** collapse `UseThisReference` sibling + nested StageTransition `LowerActionBody` (Lower) **vs** bind EF Store so `Stay.Create` is honest host (Store) | Same ranking as 2026-09-08: Lower first if only one stream | **Lower residual first**; Store bind later |
| **5. Unique-only `RestoreActionState`:** harness label vs delete once the tree is the oracle | Still Unique-gated compensation on DEI invoke | **Harness**; do not grow |
| **6. Lint-only domain-analysis mill:** admit a later suite or leave | ~half the Analysis folder by count is diagnostic packs | **Leave**; not this class |
| **7. Catalog/Ownership `Last` vs Structural first-canonical** | Analysis leftover rank 5 DEFER; merge of PR 68 kept Last | **Leave** until an explicit reopen |
| **8. Admit CURRENT?** | PIPELINE-STATUS is `(none)` | **No** — this file stays a proposal |

If Scot picks rank 2 (salvage) or rank 3 (hard reset), write that as a new note. Do not “just start deleting” from this file.

---

## How this sits next to the other consultant notes

| Note | Class | This verdict |
|------|-------|--------------|
| store-vs-lower 2026-09-08 | Where meaning lives (Lower vs Store vs DSL) | Agree. Item 5 stays PARKED. Items 2/4/6 have since landed in some form; residual is Lower dual-path + Stay.Create bind, not a DomainModeling rewrite. |
| analysis leftover 2026-09-11 | `Poly/Analysis` framework residue after sequential collapse | Agree. PR 71 closed ranks 1–3. Remaining leftovers are not Ontology. Do not fold them into a DomainModeling cut. |
| pipeline transformation 2026-09-04 | Named stages 0–5 | P1–P6 executed, **not CURRENT**. Residual stop (“simulate and emit take the same `Lower` result”) is the Lower half of rank 1, not a reset. |
| domainmodeling-simplification 2026-08-14 | Deletion-first nouns | Historical. Pack/`DomainHost` nouns largely gone. Do not reopen as a mega-delete. |
| PR 51 alignment 2026-09-04 | Dual-path as cached product | Still the honest diagnosis of `UseThisReference`. Rank 1 cuts that dual; it does not delete DomainModeling. |

---

## Non-goals of this note

- Implementing C#, deleting DomainModeling folders, or reviewing PR 72 to merge.
- Admitting PIPELINE-STATUS CURRENT (or a second CURRENT line).
- Unparking Occupancy / `BusySections`.
- Treating scratch store, `Stay.Create`, or Store job names as frozen.
- Rewriting Hotel occupancy in DEI.
- A suite README for this verdict.
