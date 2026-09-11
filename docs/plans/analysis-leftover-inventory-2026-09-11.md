# Analysis leftover inventory (PR 68 / 69 / 70 + tip residue)

**Date:** 2026-09-11
**Status:** Proposal / consultant inventory — **not CURRENT.** Do not admit a suite. Do not change [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md).
**SHA:** `e28b3f71428effec08490a60f0ec5b82a590077c` (merge of [PR 70](https://github.com/scoizzle/Poly/pull/70); includes [PR 68](https://github.com/scoizzle/Poly/pull/68) + [PR 69](https://github.com/scoizzle/Poly/pull/69))
**North star:** one sequential analysis pipeline; bags on nodes; report-order diagnostics; no analyzer-level identity collapse; product Default `Full`.
**Hard lines:** [`docs/CORE.md`](../CORE.md) §0 / §3.1 · [`docs/decisions/2026-09-04-frozen-core-pipeline.md`](../decisions/2026-09-04-frozen-core-pipeline.md)
**Excluded / PARKED:** store-vs-lower **Item 5** Occupancy / `BusySections` — do not treat as a candidate to act on in this inventory.

This note does not implement C#, does not change PIPELINE-STATUS, and is not a CURRENT suite. It inventories the **analysis-framework leftover class** after the three merged cleanups, then names more of the same class still on this tip.

**Final Boss fold-ins (suggestions only, 0 ship-blockers)** — cite exact tip path:line in the ranked table:

| Source | ID | Tip path:line |
|--------|----|---------------|
| [PR70 Final Boss follow-ups](../agent/reviews/2026-09-11-pr70-263d4c5e-final-boss-followups.md) | F1 | `Poly/Analysis/AnalysisOptions.cs:25,38-47` · `Analyzer.cs:27` |
| same | F2 | `docs/technical/syntax-analysis-framework.md:67-70` |
| same | F3 | `Poly/Analysis/AnalysisResult.cs:13` |
| same | F4–F5 | `DomainModelAnalyzer.cs:60` · `EffectAnalyzerFailClosedTests.cs:67` (+ batch) |
| [PR69 Final Boss follow-ups](../agent/reviews/2026-09-11-pr69-a3520d74-final-boss-followups.md) | F1 | `Poly/Analysis/Analyzer.cs:39` |
| same | F2 | `DomainQueries.cs:260-275` · `DomainTools.cs:276-280` · `DomainEvolution.cs:110-112` |

Note: those review files live on review branches / agent checkout; they are **sources**, not files committed on this tip.

---

## Class definition

**Analysis leftover** = residue of the *analysis framework* (`Poly/Analysis/` + always-on analysis docs + the few domain-analysis call sites those PRs touched) after sequential collapse.

It is **not** domain-pass simplification (lint-only packs, StoragePass wrapper, catalog vs capability). It is **not** lowering dual-path. It is **not** occupancy / Store.

| In class | Out of class |
|----------|----------------|
| Diagnostic identity / order / snapshot | Domain lint-only passes (`PolicyConstraintAnalyzer`, `EffectAnalyzer`, …) |
| Structural-failure API / `HasErrors` flag / early-stop modes | `session.Lower` / Store jobs / Hotel occupancy (store-vs-lower **item 5**, still parked) |
| Concurrency → sequential metadata/diagnostics | `DomainModelAnalyzer.Analyze` leftover *door* (product door is `DomainSession.Analyze`) |
| Ghost analysis APIs still advertised as live (`TryBeginAnalyzerVisit`, `AddPass`, `OptionsUsed`) | Incremental-analysis *resurrection* (already gone from C#) |
| Duplicate-name lookup identity that PR 68 advertised as fail-closed | Archive `docs/plans/archive/**` mill |

**What the three PRs already shipped (do not reopen as work):**

| PR | Claim that holds on this SHA | Evidence |
|----|------------------------------|----------|
| **68** `cleanup-analysis-foundation` | Concurrent diagnostics/metadata → sequential `List` + cloned `NodeMetadataStore`; `HasStructuralFailure` collapsed into `HasErrors`; concurrency tests deleted | `AnalysisContext.cs:9,52`; `Analyzer.cs:37`; `NodeMetadataStore.cs:21-22,31-36`; deleted `DiagnosticsConcurrencyTests` / `NodeMetadataStoreConcurrencyTests` |
| **69** `cleanup/drop-analysis-diagnostic-dedupe` | Analyzer-level `DistinctBy(Node.Id, Severity, Code, Message)` gone; report-order **multiset** | `Analyzer.cs:36-41` passes `context.Diagnostics`; `rg DistinctBy Poly/Analysis` empty; `KeepsBothInReportOrder` `Count == 2`; CORE §3.1 diagnostics row |
| **70** `cleanup/drop-report-structural-failure` | `ReportStructuralFailure` / `ShouldContinue` gone; `HasErrors` is a write-side flag; early-exit inlined | `rg ReportStructuralFailure\|ShouldContinue Poly/ Poly.Tests/` empty; `AnalysisContext.cs:70,87`; `Analyzer.cs:27-28` |

Seeds named in the consult: **DistinctBy**, **ReportStructuralFailure**, **concurrency/metadata residue**. More of the same class on this tip is ranked below.

Buckets:

| Bucket | Meaning |
|--------|---------|
| **DROP** | Delete or stop advertising. Live code/docs still name a gone API, or restoring it would undo 68/69/70. |
| **SIMPLIFY** | Keep the behavior; collapse dual representation / lying names / snapshot asymmetry. |
| **KEEP** | Shipped contract. Do not mill. |
| **DEFER** | Real, but needs a Scot call or is a different class. Do not mill as this inventory. |

---

## Ranked candidates

Rank is **severity for product-worldview fracture** if this class is ever admitted — not a queue, not chronology. Occupancy/`BusySections` (store-vs-lower Item 5) is **out of scope / PARKED** and does not appear as an action candidate.

| Rank | Candidate | Bucket | Paths | Rationale |
|------|-----------|--------|-------|-----------|
| **1** | **Stop ≡ FailFast** (PR68 Razor F3 / PR70 Final Boss **F1**) | **SIMPLIFY** | `Poly/Analysis/AnalysisOptions.cs:25,38-47`; `Analyzer.cs:27`; `docs/CORE.md:117`; tests `AnalyzerDiagnosticsTests.cs:47-73` | `ShouldStopOnStructuralErrors` is true for **both** `StopOnStructuralErrors` and `FailFast`. Both skip later passes after **any** recorded Error. Enum XML still claims Stop = “structural and reference” vs FailFast = “any error”. Product `Interpreter` / `DomainModelAnalyzer.BuildPipeline` use `.Build()` → Default **Full** (later passes still run). Tip tests prove the equivalence. Collapse to one early-stop mode **or** rewrite XML/CORE/test names so both mean “any Error”. Not fail-open. |
| **2** | **Diagnostics live alias vs metadata clone** (PR69 Final Boss **F1**) | **SIMPLIFY** | `Poly/Analysis/Analyzer.cs:39` (live `context.Diagnostics`); `:36-41` result ctor; metadata clone `:37`; `AnalysisContext.cs:52`; `AnalysisResult.cs:3-13` | PR 69 dropped DistinctBy **and** `.ToList()`. Result `Diagnostics` aliases the live `_diagnostics` list; metadata is snapshotted (`new NodeMetadataStore(context.Metadata)`). `HasErrors` on the result is computed once at construction. Production `Analyze` keeps context local. Fix: `context.Diagnostics.ToList()` (no DistinctBy) **or** document the live alias. Do **not** restore identity DistinctBy to “fix” this. |
| **3** | **`HasErrors` dual representation** (PR70 Final Boss **F3**) | **SIMPLIFY** | `AnalysisContext.cs:70,87` (write-side flag after `ShouldInclude`); `Poly/Analysis/AnalysisResult.cs:13` (`Diagnostics.Any(Error)`) | Values agree on this write path (Error always survives verbosity). Dual invites drift if a future path mutates the list without the flag (or vice versa). Construct the result from `context.HasErrors`, or document that the result always re-derives from the snapshot list. |
| **4** | **Ghost analysis APIs in always-on docs** (incl. PR70 Final Boss **F2**) | **DROP** (docs) | `docs/CORE.md:116` (`TryBeginAnalyzerVisit`); `docs/interpretation/analysis-pass-guide.md:49,79,99,171,190`; `Poly/Interpretation/Analysis/README.md:69,94`; `docs/technical/syntax-analysis-framework.md:32,40-41` **and `:67-70`** (`OptionsUsed` / `AnalysisWasTerminatedEarly` “kept” claim — members absent; tip has `Options`) | **`rg TryBeginAnalyzerVisit --glob '*.cs'` is empty** on this tip. CORE still lists it as the pass contract next to `INodeAnalyzer`. The pass guide and Interpretation README still teach `TryBeginAnalyzerVisit<T>` and `builder.AddPass(state => …)` under `using Poly.Syntax.Analysis`. `AnalysisResult.OptionsUsed` / `AnalysisWasTerminatedEarly` are **gone** from `AnalysisResult` (PR 68) but “Reviewed and Kept” in `syntax-analysis-framework.md:67-70`. `AnalysisResult.Options` remains on the record and has **zero product readers**. Drop the ghosts from CORE/guide/README; rewrite or delete the stale “kept” subsection. Archive plans may keep historical names. |
| **5** | **Duplicate-name lookup: Catalog/Ownership `Last` vs Structural first-canonical** (PR 68 F1) — **DEFER; not Occupancy Item 5** | **DEFER** | `OwnershipAggregatePass.cs:46-49` (`GroupBy` + `Last`; comment “fail-closed like DomainCatalogPass”); `DomainCatalogPass.cs:49-51,101,107,118-119` (`Last`); `StructuralDomainAnalyzer.cs:132-137` (`Skip(1)` — first canonical, error on later); `DomainSemanticLookupExtensions.cs:230`; `CapabilityAnalyzer.cs:126` | Razor: tip Last **tolerates** dups and hides Structural’s older canonical; comment is a lying invariant. Scot **merged PR 68** on `83cc6e05` with Last (align Catalog), despite Razor “ship only with F1 closed”. Evolution may temporarily hold duplicate names; Structural still reports them; `DomainEvolution.Apply` rejects on `HasErrors`. **Parked.** Do not mill Last→First or invent a throw as this leftover class. Soften the “fail-closed” comment only if a hygiene slice is admitted. Needs an explicit Scot reopen to change lookup identity. |
| **6** | **`StructuralFailure` naming residue** (PR70 Final Boss **F4–F5**) | **SIMPLIFY** | `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs:60`; `EffectAnalyzer.cs:553-556`; tests `Poly.Tests/DomainModeling/Analysis/EffectAnalyzerFailClosedTests.cs:67` (+ batch `PipelineMergeMetadataTests.cs:277`, `RuntimeContractMetadataTests.cs:157`, `DomainEvolutionApplicatorTests.cs:576`, `EmitSessionContractTests.cs:315`, `DslCompilerArtifactContributorTests.cs:79`) | API is `ReportError` / `HasErrors`. Bodies already assert Error/`HasErrors`. Names and one EffectAnalyzer comment still say StructuralFailure. Hygiene only — not a contract change. |
| **7** | **`NodeMetadataStore` µop comment** | **DROP** (comment) | `Poly/Analysis/NodeMetadataStore.cs:17-19` | Global `NodeId.Empty` bucket is **live** (catalog `SetMetadata(default, typeLookup)`). The comment still explains it as “µop generation” — primitive-IR leftover, forbidden growth. Rewrite to catalog/pass-level facts. Do not delete the empty-id fallback. |
| **8** | **Analyzer-level DistinctBy** | **KEEP** (gone) | `Poly/Analysis/Analyzer.cs:36-41`; CORE §3.1 diagnostics row | PR 69 deleted it. Contract test keeps both reports. Do **not** restore. Residual `DistinctBy` at `DomainProgramProjection.cs:120` is bound **contract endpoint names**, not diagnostics. MCP `Distinct` on stage/quantifier strings is presentation. |
| **9** | **`ReportStructuralFailure` / `ShouldContinue`** | **KEEP** (gone) | `AnalysisContext.cs` (file ends at `ClearMetadata`); PR 70 call-site renames | Live identifiers gone. Archive plans still mention the old API — historical. Do **not** restore a structural flag to make Stop≠FailFast (that is rank 1, a mode collapse, not an API restore). |
| **10** | **Sequential diagnostics + cloned metadata** | **KEEP** | `AnalysisContext.cs:9`; `NodeMetadataStore.cs:21-22,31-36`; `Poly.Tests/TestHelpers/AnalysisResultMetadata.cs` (strips bags by **copying** the store) | PR 68 product. Store is one context, one `Analyze`, not shared across threads. Fail-closed tests copy then `Remove<T>`. Do **not** restore `ConcurrentQueue` / concurrent metadata tests. Do **not** share the live store on `AnalysisResult`. |
| **11** | **Report-order diagnostic multiset** | **KEEP** | CORE §3.1; `AnalyzerDiagnosticsTests.KeepsBothInReportOrder`; `DomainQueries.GetAnalysisSummary`; MCP `AnalysisData` counts | Scot chose multiset over unique-issue collapse. `HasErrors` / `Compile` fail-closed key off **presence**. Inflated counts are noise, not fail-open. Unique-issue UX, if wanted, is presentation-layer only (rank 14). |
| **12** | **MCP `hasErrors` wire** (PR 68 F5) | **KEEP** | `Poly.Mcp/Tools/DomainTools.cs:96,1431` | Field renamed from `hasStructuralFailure`; filled from Error presence. Breaking rename already shipped. Do not restore a deprecated alias unless a live MCP client proves it. |
| **13** | **`AnalysisTelemetry` + skip/run oracles** | **KEEP** | `AnalysisTelemetry.cs`; `Analyzer.cs:23-35`; `AnalyzerDiagnosticsTests` Stop/FailFast/Full | Telemetry is how PR 70 proves later passes skipped. Product does not display it. Keep the collector; do not delete as “unused.” |
| **14** | **ErrorCount / presentation counts as multisets** (PR69 Final Boss **F2**) | **DEFER** | `Poly/DomainModeling/Queries/DomainQueries.cs:260-275` (`GetAnalysisSummary`); `Poly.Mcp/Tools/DomainTools.cs:276-280` (`hintCount`); `Poly/DomainModeling/Evolution/DomainEvolution.cs:110-112` (`ErrorCount`) | Counts are report **multisets** after DistinctBy drop. Confirm UX treats them as multisets; if unique-issue UX is required, dedupe only at UI/summary layer. Do **not** restore Analyzer DistinctBy. |
| **15** | **RequireCatalog `HasErrors` widen** (PR 68 F4) | **DEFER** | `DomainModelAnalyzer.cs:57-66` | Early-return widened from structural-only to any Error. Comment “same early-return” is inaccurate. Catalog still usually publishes; missing-catalog throw is skipped when errors exist. Product Default Full. Needs a Scot call: keep widen vs structural-only. Not rank 5 (lookup identity). |
| **16** | **`AnalysisSettings.With` / verbosity unused on product path** | **DEFER** | `AnalysisSettings.cs:29-37`; `AnalysisDiagnosticConfiguration.cs` | `With<T>` has no callers outside itself. Product analyze uses `AnalysisSettings.Default` (`Verbosity = All`, `TreatWarningsAsErrors = false`). Dormant coherent infrastructure (filter still runs on every `ReportDiagnostic`). Do not delete the filter; do not mill a verbosity product until a consumer exists. |
| **17** | **`NodeDiffUtil` vs `SyntaxDiffUtil` name** | **DEFER** | `Poly/Analysis/NodeDiffUtil.cs`; `Poly.Tests/Syntax/Analysis/SyntaxDiffUtilTests.cs`; `syntax-analysis-framework.md:31,77` | Util renamed; test class + technical doc still say SyntaxDiffUtil. Evolution consumer. Rename is hygiene, not framework residue of 68/69/70. |
| **18** | **Lint-only domain passes / StoragePass standalone** | **DEFER** (out of class) | `DomainModelAnalyzer.cs:77-80`; `StoragePass.cs:22-23,37-55`; DAS W3.2 notes | Fact vs lint split is domain-analysis simplification, not sequential-framework leftover. StoragePass has no `HasErrors` guard (PR 68 G3 comment theater). Do not admit as this parked item. |

---

## What NOT to touch

1. **Restore `ConcurrentQueue` diagnostics, concurrent metadata, or the deleted concurrency tests.** Sequential `List` + one store per `Analyze` is the shipped contract.
2. **Restore analyzer-level diagnostic `DistinctBy`.** Iterative analysis is gone. PR 69 is the contract (report-order multiset). Snapshot `ToList()` is rank 2, not DistinctBy.
3. **Restore `ReportStructuralFailure` / `HasStructuralFailure` / `ShouldContinue`.** PR 70. Rank 1 is mode/docs honesty, not a second error channel.
4. **Re-inject `EVOLUTION_STEP` / `EVOLUTION_TARGET` into `AnalysisResult.Diagnostics`.** PR 68 moved mutation failures to `EvolutionResult.MutationErrors` + `EvolutionTrace.Steps`.
5. **`DomainProgramProjection` `DistinctBy(e => e.Name)`** (`:120`) and MCP `Distinct` on names — not diagnostic leftover.
6. **Resurrect `TryBeginAnalyzerVisit` / `UseAnalyzerVisitTracking` / `IncrementalAnalysisAnalyzer` / `ShouldAnalyze` in C#.** Those APIs are already gone. Rank 4 is **doc DROP**, not a rebuild.
7. **Node replacement, bags-on-nodes, `INodeAnalyzer.Dependencies` schedule, `NodeMetadataStore` clone + inline-4, `NodeId.Empty` global bucket.** Frozen / current machinery. Clone is why fail-closed tests can strip bags.
8. **Lint-only domain-analysis campaign, StoragePass wrapper, catalog vs capability dual, `DomainModelAnalyzer.Analyze` as product door.** Different class. CORE already names `DomainSession.Analyze`.
9. **Store-vs-lower occupancy / `BusySections` (that ranking’s item 5).** Still parked. Not this leftover class.
10. **Lowering dual-path, `Stay.Create`, dict-sqlite, Stage 5c HTTP Domain walk, mut-safety, Grammar wrap-up, V3 naming, EF codegen.** THEN/PARKED/PULL elsewhere.
11. **Archive mill** (`docs/plans/archive/**` still saying `ReportStructuralFailure` / `HasStructuralFailure`). Historical.
12. **Admitting this note as PIPELINE-STATUS CURRENT.** Proposal only — Occupancy/`BusySections` Item 5 stays PARKED; this inventory is not CURRENT.

---

## Scot decisions vs defer

Landed by merge (treat as decided unless Scot reopens):

| Decision | Where | Do not |
|----------|-------|--------|
| Sequential analysis; diagnostics are a `List`; metadata store is not concurrent | PR 68 | Restore queues / concurrent tests |
| Result metadata is a **clone**, not the live context store | PR 68 `Analyzer.cs:37` | Share the live store |
| Analyzer does **not** identity-dedupe diagnostics | PR 69 | DistinctBy in `RunPasses` |
| `ReportError` is the only loud report; `HasErrors` is a flag | PR 70 | `ReportStructuralFailure` alias |
| Product pipeline is Default **Full** (no early-exit) | PR 70 oracles; `.Build()` | Make Stop/FailFast the product default |
| Catalog / Ownership lookup uses **`Last`** on duplicate names | PR 68 tip `83cc6e05` merged | Mill Last→First as leftover cleanup |
| Evolution steps/errors are **not** analysis diagnostics | PR 68 `DomainEvolution` | Re-inject EVOLUTION_* |
| MCP analysis payload field is **`hasErrors`** | PR 68 `DomainTools` | Restore `hasStructuralFailure` without a client |

Still a Scot call (defer; do not mill):

| Open | Why it is not this inventory’s IC |
|------|-----------------------------------|
| Collapse `StopOnStructuralErrors` vs `FailFast` to one mode, or keep two names that mean the same thing | Rank 1. Product never uses either. Docs/XML lie either way until someone picks. |
| Ownership/Catalog `Last` vs Structural first-canonical vs true fail-closed throw | Rank 5 **DEFER**. Razor F1 vs merge. Lookup identity is domain-catalog semantics, not framework residue. (Distinct from Occupancy/`BusySections` Item 5, which stays PARKED and is out of this class.) |
| RequireCatalog early-return: any Error vs structural-only | Rank 15. Widen already shipped; reachability is mostly catalog-still-published. |
| Frozen `ToList()` snapshot vs documented live alias | Rank 2. Not fail-open on product Analyze. |
| Unique-issue counts at MCP/query | Rank 14. Presentation. |
| Delete unread `AnalysisResult.Options` / unused `AnalysisSettings.With` | Dormant coherent fields. Delete only with a consumer audit. |

---

## Already-gone map (so agents do not “find” them as live)

| Old API / test | Tip |
|----------------|-----|
| `ConcurrentQueue<Diagnostic>` / `DiagnosticQueue` | `List<Diagnostic>` `_diagnostics` |
| `AnalysisResult` lazy DistinctBy | raw `context.Diagnostics` |
| `HasStructuralFailure` | `HasErrors` (context flag + result Any-scan) |
| `ReportStructuralFailure` / `ShouldContinue` | `ReportError` / `Analyzer` inline break |
| `AnalysisWasTerminatedEarly` / `OptionsUsed` | **gone**; `Options` still on the record, unread |
| `DiagnosticsConcurrencyTests` / `NodeMetadataStoreConcurrencyTests` | deleted; schedule + keep-both oracles remain |
| `TryBeginAnalyzerVisit` / `UseAnalyzerVisitTracking` / incremental `Analyze` overload | **no C#**; docs still teach them (rank 4) |

---

## Non-goals of this note

- Implementing C# or admitting a leftover-cleanup suite.
- Changing PIPELINE-STATUS (CURRENT stays `(none)`).
- Reopening PR 68 F1 lookup identity.
- Reopening DistinctBy / ReportStructuralFailure / concurrent stores.
- Occupancy DSL, Store vs Lower, dict-sqlite, lint-only domain passes.
- A second CURRENT or a suite README for this inventory.
