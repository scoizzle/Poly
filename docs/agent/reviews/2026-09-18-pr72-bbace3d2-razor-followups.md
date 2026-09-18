# PR 72 Razor follow-ups — 2026-09-18

Source: `docs/agent/reviews/2026-09-18-pr72-bbace3d2-razor.md`  
Tip: `bbace3d2aae4141df65536eeedec1afe2a389723`  
Prior follow-ups: none dispositioned for this tip (first Razor pass on PR 72).

## Open

- [ ] **F1** — `docs/interpretation/analysis-pass-guide.md:10` — Remove ghost `After` / `Before` Overview sentence; state that `AnalyzerBuilder.Build` is registration order (align with CORE + `Poly/Interpretation/Analysis/README.md`). Also fix lifecycle “scheduled order” at line 15 (**Issue 7**).

- [ ] **F2** — `docs/interpretation/README.md:55-56` — Replace “declare dependencies” / “topological sort” with registration-order wording; link Analysis README pass list.

- [ ] **F3** — `src/Poly.DslCompiler/HttpFileGenerator.cs:199-219` — Drive HTTP sample literals from session CLR maps (`RuntimeAnalysisCache.ClrTypeName` / TypeMappingRegistry), not hard-coded domain `Date`/`DateTime` lists (sibling to MinimalApiGenerator cleanup).

- [ ] **F4** — `Poly/DomainModeling/Analysis/OwnershipAggregatePass.cs:48` — Residual PR68 F1: duplicate entity `GroupBy` → `g.Last()`. Decide fail-closed vs documented Last-wins; add oracle if policy changes. Not introduced by PR 72.

- [ ] **F5** — `Poly/DomainModeling/Meaning/DomainTypeMapping.cs` + `RuntimeAnalysisCache.ClrTypeName` — Contract for Date/DateTime when TypeMaps absent or `domain` null (today: passthrough / SQL `varchar` / `Prim.Structure`). Prefer fail-closed or explicit unsupported; add strip-maps / null-domain test.

- [ ] **F6** — `PersistenceEmitLibrary` + vendor packs — Test that loading `persistence` and `sqlite` (or two StoragePass registrars) fails closed with PassName conflict; optionally clearer catalog mutual-exclusion message.

## Process

- [ ] **F7** — When a PR’s AC includes “no ghost Before/After/TemporalPass in live docs”, gate with `rg` over `docs/**/*.md` excluding `docs/plans/archive` for `After` / `Before` schedule language and `TemporalPass` before merge.

## Fixed / invalid this pass

(none — first review of this tip)
