# PR 72 Final Boss follow-ups — re-verify dea21732 — 2026-09-20

Source: `docs/agent/reviews/2026-09-20-pr72-dea21732-final-boss.md`  
Tip: `dea217323fdcddadb919b89ad22d127e89d90e66`  
Prior: `docs/agent/reviews/2026-09-18-pr72-d176f213-final-boss.md` + `docs/agent/reviews/2026-09-18-pr72-d176f213-final-boss-followups.md` (not overwritten)  
Model: grok-4.6

## Disposition of prior Final Boss items (`d176f213` claims vs this SHA)

- [x] **F1** — **bug** — **fixed on named files** (already at `d176f213`) — `docs/interpretation/analysis-pass-guide.md:10-11,16,149` still registration order. Unchanged vs `d176f213`.
- [x] **F2** — **bug** — **fixed** (already at `d176f213`) — `docs/interpretation/README.md:55-57` registration order / no topological insert.
- [ ] **F3** — **suggestion** — **still open** — `HttpFileGenerator.cs:199-219` domain `Date`/`DateTime` sample literals (product unchanged vs `d176f213` and vs master; file not in this PR).
- [ ] **F4** — **suggestion** — **still open** — `OwnershipAggregatePass.cs:48` still `g.Last()` (product unchanged).
- [ ] **F5** — **suggestion** — **still open** — `DomainTypeMapping` null-domain / missing TypeMaps passthrough (product unchanged).
- [ ] **F6** — **suggestion** — **still open** — persistence + sqlite dual `StoragePass` load untested (product unchanged).
- [ ] **F7** — **process** — **still open** — honesty `rg` should cover `docs/interpretation/**/*.md` **and** `Poly/Interpretation/**/*.md`. This SHA’s F8 close would have been caught by that gate. Still would miss `AddPass` (F10).
- [x] **F8** — **bug** — **CLOSED** — `Poly/Interpretation/README.md:113` is `Use*` registration = run order; no topological insert / insert≠registration. `:338` is `PassName` + `Analyze()`; schedule is `Use*` / `AddAnalyzer`; parenthetical “no `After` / `Before` / `Dependencies`” is negation, not API. Evidence: `git show dea21732:Poly/Interpretation/README.md` those lines; vs `d176f213` the F8 hunk.
- [x] **F9** — **nit** — **CLOSED on named file** — `Poly/Interpretation/Analysis/README.md:93` is `builder.AddAnalyzer(new MyPass());`. Sibling leftover F10.

## Open (this tip)

- [ ] **F3** — `src/Poly.DslCompiler/HttpFileGenerator.cs:199-219` — Drive HTTP sample literals from session CLR maps (`RuntimeAnalysisCache.ClrTypeName` / TypeMappingRegistry), not hard-coded domain `Date`/`DateTime` lists (sibling to MinimalApiGenerator).

- [ ] **F4** — `Poly/DomainModeling/Analysis/OwnershipAggregatePass.cs:48` — Residual PR68 F1: duplicate entity `GroupBy` → `g.Last()`. Decide fail-closed vs documented Last-wins; add oracle if policy changes. Not introduced by PR 72. Comment `:45` “fail-closed like DomainCatalogPass” is Last-wins (catalog does the same).

- [ ] **F5** — `Poly/DomainModeling/Meaning/DomainTypeMapping.cs` + `RuntimeAnalysisCache.ClrTypeName` — Contract for Date/DateTime when TypeMaps absent or `domain` null (today: passthrough / SQL `varchar`). Prefer fail-closed or explicit unsupported; add strip-maps / null-domain test.

- [ ] **F6** — `PersistenceEmitLibrary` + vendor packs — Test that loading `persistence` and `sqlite` (or two StoragePass registrars) fails closed with PassName conflict; optionally clearer catalog mutual-exclusion message.

- [ ] **F10** — **nit** — `docs/interpretation/analysis-pass-guide.md:99` — Tutorial `AddPass(state => new WidgetAnalyzer())` is not an API. Change to `AddAnalyzer(new WidgetAnalyzer())`. Same class as closed F9; this file was not in `dea21732`. Do not block ship.

## Process

- [ ] **F7** — When a PR’s AC includes “no ghost Before/After/TemporalPass in live interpretation docs”, gate with `rg` over `docs/interpretation/**/*.md` **and** `Poly/Interpretation/**/*.md` (exclude `docs/plans/archive`, `docs/agent/reviews`) for schedule `After` / `Before`, `topological insert`, `topological sort`, and `TemporalPass` before merge. Optional: also `AddPass(` if tutorial-API honesty is in AC. `docs/`-only grep missed F8 at `d176f213`; this SHA closed F8 without adding the gate.

## Ship gate

**ship** — F8 closed at `Poly/Interpretation/README.md:113` and `:338`. F9 closed at `Poly/Interpretation/Analysis/README.md:93`. Product extract vs master is fail-closed on Meaning / Storage overlay / duplicate PassName (unchanged vs `d176f213`). F3–F7 stay suggestions/process. F10 nit does not reopen F8. Foreman may merge.
