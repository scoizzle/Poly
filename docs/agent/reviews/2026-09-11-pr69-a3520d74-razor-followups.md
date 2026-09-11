# PR 69 follow-ups (Razor) — 2026-09-11

Tip: `a3520d74f1bf288d485f3f3bf453090e9fef4a48`  
Review: `docs/agent/reviews/2026-09-11-pr69-a3520d74-razor.md`  
Verdict: ship (0 bugs)

## Open

- [ ] **F1** — `Poly/Analysis/Analyzer.cs:39` — Restore a report-order **snapshot** without reintroducing DistinctBy: pass `context.Diagnostics.ToList()` into `AnalysisResult` (or document live-list alias vs metadata snapshot). Do not add analyzer-level diagnostic dedupe.
- [ ] **F2** — Presentation counts — Confirm MCP / `DomainQueries.GetAnalysisSummary` / `DomainEvolution` treat diagnostic counts as **report multisets**. If unique-issue UX is required, dedupe only at the UI/summary layer; keep `Analyzer` free of DistinctBy.
- [ ] **F3** — `Poly.Tests/Syntax/Analysis/AnalyzerDiagnosticsTests.cs` — Strengthen `KeepsBothInReportOrder` with two distinguishable messages/codes and assert index order.

## Disposition (priors)

No prior PR69 review follow-ups in-tree. `07955df8` DistinctBy restore is superseded by this tip by design — not reopened.

## Process

- [ ] **P1** — When removing a collapse/filter at an API boundary, decide explicitly: snapshot copy vs live alias; call it out in CORE if both metadata and diagnostics are published from the same run.
