# PR 72 Final Boss follow-ups — re-verify d176f213 — 2026-09-18

Source: `docs/agent/reviews/2026-09-18-pr72-d176f213-final-boss.md`  
Tip: `d176f213caa916ca23179657d960526e5315a0d2`  
Prior: `docs/agent/reviews/2026-09-18-pr72-d176f213-razor.md` + `docs/agent/reviews/2026-09-18-pr72-d176f213-razor-followups.md`  
Model: grok-4.6

## Disposition of Razor items (`d176f213`)

- [x] **F1** — **bug** — **fixed on named files** — `docs/interpretation/analysis-pass-guide.md:10-11,16` states `Build` is registration order (`Use*` chain). Broader “no After/Before on live interpretation docs” **rejected** — see **F8**.
- [x] **F2** — **bug** — **fixed** — `docs/interpretation/README.md:55-57` registration order / no topological insert; links Analysis README pass list.
- [ ] **F3** — **suggestion** — **still open** — `HttpFileGenerator.cs:199-219` domain `Date`/`DateTime` sample literals (product unchanged).
- [ ] **F4** — **suggestion** — **still open** — `OwnershipAggregatePass.cs:48` still `g.Last()` (product unchanged).
- [ ] **F5** — **suggestion** — **still open** — `DomainTypeMapping` null-domain / missing TypeMaps passthrough (product unchanged).
- [ ] **F6** — **suggestion** — **still open** — persistence + sqlite dual `StoragePass` load untested (product unchanged).
- [ ] **F7** — **process** — **still open / too narrow** — `rg` over `docs/**/*.md` excluding archive would still miss `Poly/Interpretation/README.md`. Widen when closing F8.

## Open (this tip)

- [ ] **F8** — **bug** — **ship-blocker** — `Poly/Interpretation/README.md:113` still says built order is after `AnalyzerBuilder` topological insert and “insert order ≠ registration order.” `:338` (this PR vs master) tells authors to implement `PassName`, `After` / `Before`. `INodeAnalyzer` has neither. Rewrite `:113` to: 14 passes; `Use*` registration is run order; see Analysis README list. Rewrite `:338` to `PassName` + `Analyze()`; schedule is the `Use*` / `AddAnalyzer` line. No After/Before/Dependencies/topological insert.

- [ ] **F3** — `src/Poly.DslCompiler/HttpFileGenerator.cs:199-219` — Drive HTTP sample literals from session CLR maps (`RuntimeAnalysisCache.ClrTypeName` / TypeMappingRegistry), not hard-coded domain `Date`/`DateTime` lists (sibling to MinimalApiGenerator).

- [ ] **F4** — `Poly/DomainModeling/Analysis/OwnershipAggregatePass.cs:48` — Residual PR68 F1: duplicate entity `GroupBy` → `g.Last()`. Decide fail-closed vs documented Last-wins; add oracle if policy changes. Not introduced by PR 72. Comment `:45` “fail-closed like DomainCatalogPass” is Last-wins (catalog does the same).

- [ ] **F5** — `Poly/DomainModeling/Meaning/DomainTypeMapping.cs` + `RuntimeAnalysisCache.ClrTypeName` — Contract for Date/DateTime when TypeMaps absent or `domain` null (today: passthrough / SQL `varchar` / `Prim.Structure`). Prefer fail-closed or explicit unsupported; add strip-maps / null-domain test.

- [ ] **F6** — `PersistenceEmitLibrary` + vendor packs — Test that loading `persistence` and `sqlite` (or two StoragePass registrars) fails closed with PassName conflict; optionally clearer catalog mutual-exclusion message.

- [ ] **F9** — **nit** — `Poly/Interpretation/Analysis/README.md:93` — Tutorial `AddPass(state => new MyPass())` is not an API. Change to `AddAnalyzer(new MyPass())` with F8.

## Process

- [ ] **F7** — When a PR’s AC includes “no ghost Before/After/TemporalPass in live interpretation docs”, gate with `rg` over `docs/interpretation/**/*.md` **and** `Poly/Interpretation/**/*.md` (exclude `docs/plans/archive`, `docs/agent/reviews`) for schedule `After` / `Before`, `topological insert`, `topological sort`, and `TemporalPass` before merge. `docs/`-only grep missed F8.

## Ship gate

**not ship** — Razor F1 (named `analysis-pass-guide.md`) and F2 are closed at `d176f213`; product extract vs `bbace3d2` is unchanged and vs master is fail-closed on Meaning / Storage overlay / duplicate PassName. Open **F8** is a live Interpretation README scheduler ghost this PR introduced at `:338` and left at `:113`. Close F8 (and optionally F9 in the same docs hunk) before merge. F3–F7 stay suggestions/process and do not block once F8 is honest.
