# PR 69 Final Boss Follow-ups — 2026-09-11

- **Review**: [`2026-09-11-pr69-a3520d74-final-boss.md`](2026-09-11-pr69-a3520d74-final-boss.md)
- **PR**: [#69](https://github.com/scoizzle/Poly/pull/69) (SHA `a3520d74f1bf288d485f3f3bf453090e9fef4a48`)
- **Mode**: re-verify of Razor `docs/agent/reviews/2026-09-11-pr69-a3520d74-razor-followups.md`
- **Model**: grok-4.6
- **Open bugs**: none
- **Verdict**: ship — do not block on F3

## Follow-up Tasks

- [ ] **F1** — **suggestion** — `Poly/Analysis/Analyzer.cs:39` — Restore a report-order **snapshot** without reintroducing DistinctBy: pass `context.Diagnostics.ToList()` into `AnalysisResult` (or document live-list alias vs `new NodeMetadataStore(context.Metadata)` clone, including that `AnalysisResult.HasErrors` is frozen at construction). Do not add analyzer-level diagnostic dedupe.

- [ ] **F2** — **suggestion** — Presentation counts — Confirm MCP (`DomainTools.cs:276-280`) / `DomainQueries.GetAnalysisSummary` (`DomainQueries.cs:260-275`) / `DomainEvolution` (`DomainEvolution.cs:110-112`) treat diagnostic counts as **report multisets**. If unique-issue UX is required, dedupe only at the UI/summary layer; keep `Analyzer` free of DistinctBy.

- [ ] **F3** — **nit** — `Poly.Tests/Syntax/Analysis/AnalyzerDiagnosticsTests.cs:5-15` — Strengthen `KeepsBothInReportOrder` with two distinguishable messages/codes and assert index order. Keep `Count == 2`. Not a merge bar.

## Process

- [ ] **P1** — When removing a collapse/filter at an API boundary, decide explicitly: snapshot copy vs live alias; call it out in CORE if metadata is cloned and diagnostics are not (Razor P1, still valid).

## Prior Follow-ups (Razor `a3520d74`)

- **Razor F1** (live `context.Diagnostics` alias) — **still open** → this file **F1**. Re-verified: `Analyzer.cs:39` still passes `context.Diagnostics`; metadata still cloned at `:37`.
- **Razor F2** (presentation counts as multisets) — **still open** → this file **F2**. Re-verified: `GetAnalysisSummary` / MCP `hintCount` / evolution `ErrorCount` still `.Count` / `.Count(severity)`.
- **Razor F3** (order weakly evidenced) — **still open** → this file **F3**. Re-verified: both reports still identical `DUP` / `"Duplicate diagnostic"`. Foreman: do not block ship.
- Razor “open bugs: none / ship” — **stands**. No new bug on this SHA.
- Razor DistinctBy-in-projection “entity names” — **corrected**: `DomainProgramProjection.cs:120` DistinctBy is bound **contract endpoint** names, still not diagnostic dedupe. No follow-up task.
