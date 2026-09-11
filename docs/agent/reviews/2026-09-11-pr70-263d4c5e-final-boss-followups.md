# PR 70 follow-ups (Final Boss) — 2026-09-11

Tip: `263d4c5e20c8be0517a96740b035a1d3517d4f73`  
Review: `docs/agent/reviews/2026-09-11-pr70-263d4c5e-final-boss.md`  
Prior: `docs/agent/reviews/2026-09-11-pr70-263d4c5e-razor.md` + `…-razor-followups.md` (not overwritten)  
Mode: re-verify  
Model: grok-4.6  
Verdict: **ship** (0 bugs)

## Open

- [ ] **F1** — **suggestion** — `Poly/Analysis/AnalysisOptions.cs:25,38-47` (+ `Analyzer.cs:27`). Restore distinction between `StopOnStructuralErrors` and `FailFast`, **or** delete one mode and update enum XML / CORE / tests. Tip proves both skip later passes identically on `HasErrors`; Full still runs later passes. Product Default Full does not take this door. (Carries PR68 Razor F3 / PR70 Razor F1.)
- [ ] **F2** — **suggestion** — `docs/technical/syntax-analysis-framework.md:67-70`. Remove or rewrite the `OptionsUsed` / `AnalysisWasTerminatedEarly` “kept” claim — those members are absent on tip `AnalysisResult` (record has `Options`, not `OptionsUsed`).
- [ ] **F3** — **suggestion** — `Poly/Analysis/AnalysisResult.cs:13`. Prefer seeding result `HasErrors` from `context.HasErrors` (or document re-derive-from-list as the only source of truth) so flag vs Any-scan cannot drift.
- [ ] **F4** — **nit** — `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs:60`. Drop stale `HasStructuralFailure` wording in the RequireCatalog comment.
- [ ] **F5** — **nit** — `Poly.Tests/DomainModeling/Analysis/EffectAnalyzerFailClosedTests.cs:67` (optional batch: `PipelineMergeMetadataTests.cs:277`, `RuntimeContractMetadataTests.cs:157`, `DomainEvolutionApplicatorTests.cs:576`, `EmitSessionContractTests.cs:315`, `DslCompilerArtifactContributorTests.cs:79`, plus `EffectAnalyzer.cs:553-556` comment). Rename to HasErrors/Error wording; bodies already assert correctly.
- [ ] **F6** — **nit** — `Poly/Analysis/AnalysisContext.cs`, `Poly/Analysis/AnalyzerBuilder.cs`. Add trailing newlines at EOF.

## Disposition (priors)

| Prior | Disposition on tip `263d4c5e` |
|-------|-------------------------------|
| PR70 Razor **F1** (Stop ≡ FailFast) | **still open** → this file **F1**. Re-proved: `ShouldStopOnStructuralErrors` is `Stop or FailFast`; both tests skip; Full runs later. Not fail-open. |
| PR70 Razor **F2** (OptionsUsed / AnalysisWasTerminatedEarly) | **still open** → **F2**. `AnalysisResult.cs` has neither property. |
| PR70 Razor **F3** (flag vs Any-scan) | **still open** → **F3**. Context `:70,87` vs result `:13`. Consistent on this write path. |
| PR70 Razor **F4** (DomainModelAnalyzer comment) | **still open** → **F4**. |
| PR70 Razor **F5** (StructuralFailure test names) | **still open** → **F5**. |
| PR70 Razor **F6** (EOF newlines) | **still open** → **F6**. `endswith_nl False` this session. |
| PR70 Razor “0 bugs / ship” | **stands**. No new bug. Do not block ship on F1–F6. |
| PR68 Razor **F3** (Stop ≡ FailFast via HasErrors) | **still open** → **F1**. |
| PR68 Razor **F4** (RequireCatalog HasErrors widen) | **still open** (out of PR70 delta). Not re-litigated as a tip bug. |
| PR69 DistinctBy follow-ups | **N/A** — PR69 not in tip ancestry vs this master; tip still has DistinctBy. |

## Process

- [ ] **P1** — When collapsing two analysis modes onto one predicate, update enum XML and CORE in the **same** change, or delete the redundant mode. Recurring: Stop vs FailFast docs lag the code (PR68→PR70).
- [ ] **P2** — When renaming a public diagnostic helper (`ReportStructuralFailure` → `ReportError`), batch-rename test method names and nearby comments that still encode the old API word so greps stay honest.
