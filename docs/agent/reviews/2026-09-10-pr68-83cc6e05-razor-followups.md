# PR 68 Razor follow-ups — 2026-09-10

- **Review**: [`2026-09-10-pr68-83cc6e05-razor.md`](2026-09-10-pr68-83cc6e05-razor.md)
- **PR**: [#68](https://github.com/scoizzle/Poly/pull/68)
- **Tip SHA**: `83cc6e050f5187cc5027ff71f802362a54080dc2`
- **Prior assign**: `07955df8` HOLD/superseded — dispositions below are against **this tip**, not that SHA alone
- **Open bugs**: 1 (F1) — ship only with F1 closed
- **Open suggestions**: F2–F5; nits F6–F7

## Follow-up Tasks

- [ ] **F1** — **bug** — `Poly/DomainModeling/Analysis/OwnershipAggregatePass.cs:46-49`. Remove false “fail-closed like DomainCatalogPass” claim. Choose one contract and implement it: (1) real fail-closed on duplicate type names in ownership (and ideally catalog), or (2) tolerate-dup with **First** (match `StructuralDomainAnalyzer` Skip(1) canonical) or **Last** (match `DomainCatalogPass`) **documented honestly**. Add `BuildAggregate` test with two `Entity("Item", …)` asserting which instance is in the lookup. Tip `83cc6e05` Last hides the older/first entity when evolution appends a dup.

- [ ] **F2** — **suggestion** — `Poly.Tests/DomainModeling/Analysis/PipelineMergeMetadataTests.cs:278-280` vs `Poly/DomainModeling/Analysis/StoragePass.cs:36-55`. G3 claims a HasErrors guard that does not exist. Fix comment to real reason (topology+aggregate still published) or restore an explicit guard and assert it.

- [ ] **F3** — **suggestion** — `Poly/Analysis/AnalysisContext.cs:129-130`, `Poly/Analysis/AnalysisOptions.cs`. Restore distinction between `StopOnStructuralErrors` and `FailFast` (or delete one mode and update docs). Tip `HasErrors` stop makes Stop ≡ any-Error when that options bit is set.

- [ ] **F4** — **suggestion** — `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs:60-62`. Document or narrow RequireCatalog early-return (`HasErrors` vs structural-only). Add contract test for HasErrors + missing catalog.

- [ ] **F5** — **suggestion** — `Poly.Mcp/Tools/DomainTools.cs:96,359-362`. MCP JSON `hasStructuralFailure` → `hasErrors` is a wire break; alias or changelog.

- [ ] **F6** — **nit** — Correct PR/tip notes: DistinctBy restored in `07955df8`, present on tip; `83cc6e05` Ownership-only.

- [ ] **F7** — **nit** — `docs/CORE.md:119` — clarify filter-at-report + Analyzer DistinctBy vs “filtered on AnalysisResult”.

## Process

- [ ] **P1** — Tip/fix commit messages that claim “fail-closed” or “restore X” must match the file delta (`git show --stat`). Recurring: marketing claims force reviewers to re-prove ancestry.

- [ ] **P2** — When collapsing structural vs any-Error flags, update every invariant-stating comment and mode enum doc in the same change; sibling Catalog vs Structural First/Last must be one checklist item for dup-name work.

## Disposition of superseded `07955df8` concerns (tip re-verify)

| Concern | Tip disposition |
|---------|-----------------|
| DistinctBy missing after `a717` | **fixed on tip** (`Analyzer.cs:36-39`, from `07955df8`) |
| Ownership `ToDictionary` throw after early-return removal | **mitigated** via GroupBy; tip uses Last (see F1) |
| RequireCatalog after HasStructuralFailure removal | **present** as HasErrors early-return; widen tracked as F4 |
| File bugs against `07955df8` alone | **invalid** for this review — tip includes that commit |

## Non-goals (do not reopen as tip bugs)

- Re-adding concurrent diagnostics/metadata tests without a concurrent Analyzer.
- Claiming DistinctBy absent on `83cc6e05`.
- Claiming Ownership still uses First on tip.
