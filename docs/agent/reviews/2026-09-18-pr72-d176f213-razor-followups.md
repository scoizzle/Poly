# PR 72 Razor follow-ups — re-verify d176f213 — 2026-09-18

Source: `docs/agent/reviews/2026-09-18-pr72-d176f213-razor.md`  
Tip: `d176f213caa916ca23179657d960526e5315a0d2`  
Prior follow-ups: `/workspace/Poly-pr72-bbace3d2/docs/agent/reviews/2026-09-18-pr72-bbace3d2-razor-followups.md` (`bbace3d2aae4141df65536eeedec1afe2a389723`)

## Disposition of prior items (`bbace3d2`)

- [x] **F1** — **bug** — **fixed** @ `d176f213` — `docs/interpretation/analysis-pass-guide.md:10-11` states `Build` is registration order (`Use*` chain is the schedule). Lifecycle `:16` “scheduled order” → “registration order” (prior Issue 7). Aligns with `AnalyzerBuilder.cs` + CORE + Analysis README.
- [x] **F2** — **bug** — **fixed** @ `d176f213` — `docs/interpretation/README.md:55-57` registration order / no topological insert; links Analysis README pass list.
- [ ] **F3** — **suggestion** — **still open** — `HttpFileGenerator.cs:199-219` still hard-codes domain `Date`/`DateTime` sample literals (product unchanged).
- [ ] **F4** — **suggestion** — **still open** — `OwnershipAggregatePass.cs:48` still `g.Last()` (product unchanged).
- [ ] **F5** — **suggestion** — **still open** — `DomainTypeMapping` null-domain / missing TypeMaps passthrough (product unchanged).
- [ ] **F6** — **suggestion** — **still open** — persistence + sqlite dual `StoragePass` load untested (product unchanged).
- [ ] **F7** — **process** — **still open** — no `rg` gate over live docs for ghost Before/After / TemporalPass (docs-only tip did not add CI).

## Open (this tip)

- [ ] **F3** — `src/Poly.DslCompiler/HttpFileGenerator.cs:199-219` — Drive HTTP sample literals from session CLR maps (`RuntimeAnalysisCache.ClrTypeName` / TypeMappingRegistry), not hard-coded domain `Date`/`DateTime` lists (sibling to MinimalApiGenerator cleanup).

- [ ] **F4** — `Poly/DomainModeling/Analysis/OwnershipAggregatePass.cs:48` — Residual PR68 F1: duplicate entity `GroupBy` → `g.Last()`. Decide fail-closed vs documented Last-wins; add oracle if policy changes. Not introduced by PR 72.

- [ ] **F5** — `Poly/DomainModeling/Meaning/DomainTypeMapping.cs` + `RuntimeAnalysisCache.ClrTypeName` — Contract for Date/DateTime when TypeMaps absent or `domain` null (today: passthrough / SQL `varchar` / `Prim.Structure`). Prefer fail-closed or explicit unsupported; add strip-maps / null-domain test.

- [ ] **F6** — `PersistenceEmitLibrary` + vendor packs — Test that loading `persistence` and `sqlite` (or two StoragePass registrars) fails closed with PassName conflict; optionally clearer catalog mutual-exclusion message.

## Process

- [ ] **F7** — When a PR’s AC includes “no ghost Before/After/TemporalPass in live docs”, gate with `rg` over `docs/**/*.md` excluding `docs/plans/archive` for `After` / `Before` schedule language and `TemporalPass` before merge.

## Ship gate

**ship** — prior **F1** and **F2** closed on live interpretation docs at `d176f213`; no product regression vs `bbace3d2`. Open items are suggestions (F3–F6) and process (F7) only.
