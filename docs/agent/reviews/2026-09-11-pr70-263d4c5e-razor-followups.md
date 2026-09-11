# PR 70 follow-ups (Razor) — 2026-09-11

Tip: `263d4c5e20c8be0517a96740b035a1d3517d4f73`  
Review: `docs/agent/reviews/2026-09-11-pr70-263d4c5e-razor.md`  
Verdict: **ship** (0 bugs)

## Open

- [ ] **F1** — **suggestion** — `Poly/Analysis/AnalysisOptions.cs:25,38-47` (+ `Analyzer.cs:27`). Restore distinction between `StopOnStructuralErrors` and `FailFast`, **or** delete one mode and update enum XML / CORE / tests. Tip proves both skip later passes identically on `HasErrors`; Full still runs later passes. (Carries PR68 Razor F3.)
- [ ] **F2** — **suggestion** — `docs/technical/syntax-analysis-framework.md:67-70`. Remove or rewrite the `OptionsUsed` / `AnalysisWasTerminatedEarly` “kept” claim — those members are absent on tip `AnalysisResult`.
- [ ] **F3** — **suggestion** — `Poly/Analysis/AnalysisResult.cs:13`. Prefer seeding result `HasErrors` from `context.HasErrors` (or document re-derive-from-list as the only source of truth) so flag vs Any-scan cannot drift.
- [ ] **F4** — **nit** — `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs:60`. Drop stale `HasStructuralFailure` wording in the RequireCatalog comment.
- [ ] **F5** — **nit** — `Poly.Tests/DomainModeling/Analysis/EffectAnalyzerFailClosedTests.cs:67` (optional batch of other `*StructuralFailure*` test names). Rename to HasErrors/Error wording; body already asserts correctly.
- [ ] **F6** — **nit** — `Poly/Analysis/AnalysisContext.cs`, `Poly/Analysis/AnalyzerBuilder.cs`. Add trailing newlines at EOF.

## Disposition (priors)

| Prior | Disposition on tip `263d4c5e` |
|-------|-------------------------------|
| PR68 Razor **F3** (Stop ≡ FailFast via HasErrors) | **still open** → this review **F1**. Tip inlines same predicate; adds tests proving equivalence; does not restore distinction. |
| PR68 Razor **F4** (RequireCatalog HasErrors widen) | **still open** (out of PR70 delta). DomainModelAnalyzer comment remains; not re-litigated as a tip bug. |
| PR68 Razor F1/F2/F5–F7 | Not re-verified as part of this PR70 scope (Ownership/StoragePass/MCP wire/CORE DistinctBy wording). |
| PR69 DistinctBy follow-ups | **N/A** — PR69 not in tip ancestry vs this master; tip still has DistinctBy. |

## Process

- [ ] **P1** — When collapsing two analysis modes onto one predicate, update enum XML and CORE in the **same** change, or delete the redundant mode. Recurring: Stop vs FailFast docs lag the code (PR68→PR70).
- [ ] **P2** — When renaming a public diagnostic helper (`ReportStructuralFailure` → `ReportError`), batch-rename test method names that still encode the old API word so greps stay honest.
