# PR 72 library extract — Razor phenomenal-review — 2026-09-18

- **Target**: PR 72 (`scoizzle/Poly`), tip `bbace3d2aae4141df65536eeedec1afe2a389723`
- **Branch under review**: `review/razor-pr72-bbace3d2` (workspace tip matches PR head)
- **Diff base**: `origin/master` merge-base `934b2409818041df2975ac8dea62c5a399283040` — 126 files, +1800/−1436
- **Mode**: standard (adversarial, review-only; no product code; no merge)
- **CI / mergeability** (verified `gh pr view 72`): SUCCESS · MERGEABLE · CLEAN
- **Issue counts**: 2 bugs, 4 suggestions, 1 nit
- **Verdict**: ship only with F1–F2 closed (live docs still advertise Before/After + topological schedule)
- **Process notes**: CORE and Interpretation Analysis README were updated for registration-order; `docs/interpretation/analysis-pass-guide.md` Overview and `docs/interpretation/README.md` Key Concepts were left on the old scheduler story — same class as “plan/docs lies” on a PR whose AC is doc honesty.

## Summary

PR 72 removes mid-list library splice (`Before`/`After` / `Dependencies` insert) and deletes `TemporalPass` in favor of session `ExpressionMeaning` + folds/type maps. `StoragePass` / `PersistenceSurfacePass` become persistence/vendor overlays appended after the core list. Host CLR/SQL projections for temporal names move onto `TypeMappingRegistry`; core `DomainTypeMapping` keeps Text/Number/Boolean/Uuid/Binary only. Code and pipeline oracles largely match the PR claims (registration order, no Temporal pass, StoragePass gated, clock assign via `TryClaimAssign`, packs Apply maps). Dominant residual risk is **live-doc ghost scheduler text** contradicting the change, plus incomplete Http sample-type lists and null-domain CLR fallback softness — not a green-CI illusion on the main domain/temporal paths.

## Checklist (protocol §9)

- [x] Diff collected; scope drift noted (no wal/shm; no stale plans examination notes in tip)
- [x] Stance adversarial; primary greps/reads this session
- [x] Producer/consumer traced for Meaning / TypeMaps / ExtraAnalyzers
- [x] Sibling-path: core `Library()`/`MapChildren`/`Meaning.Lowering` vs deleted TemporalPass; StoragePass core vs overlay
- [x] Fail-loud reachability for duplicate AddAnalyzer / missing Storage bags
- [x] Oracles audited (PassDependencyDeclaration, AnalyzerDependencySchedule, TemporalGolden, ClockLowering, StandardAnalyzer)
- [x] Review + follow-ups written under `docs/agent/reviews/`

## Adversarial focus — dispositions

### 1. AnalyzerBuilder — registration order / duplicates / Interpretation

**Proven.** `Poly/Analysis/AnalyzerBuilder.cs`: `OrderedDictionary` append; `Build` = `_entries.Values`; duplicate `PassName` / empty name fail closed. Old tip used dependency **insert** (`Max(Dependencies index)+1`); that API is gone from `INodeAnalyzer`.

Domain schedule is literal registration in `UseDomainModelAnalysisPipeline` (`DomainModelAnalyzer.cs:70-89`); libraries append via `SessionBuilder.AddAnalyzer` then `BuildPipeline(extraAnalyzers, …)`.

Interpretation: all `Dependencies =>` removed from Interpretation analysis passes; `Interpreter.cs` **reorders** `Use*` so registration matches the former dependency-insert outcome (Lambda early; ExceptionRegion after CFG; CallSite/DefiniteAssignment late). Oracle: `StandardAnalyzer_PassNames_MatchInterpreterPipeline` (`InterpretationStabilizationTests.cs:449`) asserts exact `PassNames` == Interpreter list.

### 2. TemporalPass deleted → Meaning wiring

**Proven.** `TemporalPass.cs` deleted (was `Id = "Temporal"`). `TemporalLibrary.Register` only folds/print/Meaning/`TemporalTypeMaps` — **no** `AddAnalyzer`. `TemporalMeaning.Register` + `TemporalLowering.Register` wire inference/checks/assign/lowering/defaults.

`DomainPipeline_HasNoTemporalPass` asserts `!order.Contains("Temporal")` — matches old `PassName`, not theater. Sibling: `DomainSessionTests.Analyze_TemporalLibrary_RegistersMeaningNotAPass` also asserts Meaning handlers present and telemetry has no Temporal.

### 3. StoragePass overlay only

**Proven.** Core pipeline comment + list omit StoragePass (`DomainModelAnalyzer.cs:14-15,70-89`). Registration only from `PersistenceEmitLibrary` (`persistence`) and vendor packs (`SqlitePack`/`MySqlPack`/`SqlServerPack`). `DomainPipeline_HasNoStoragePass_WithoutPersistenceLibrary` asserts absence on empty-extension domain. With `uses persistence`, `PassDependencyDeclarationTests` asserts OwnershipAggregate / EffectInvariant before StoragePass.

### 4. ExpressionMeaning / TryClaimAssign

**Proven.** `ExpressionTypeAnalyzer.CheckCompatible` calls `_meaning.TryClaimAssign` first; on miss falls through to `Compatible` (fail-closed for unclaimed Now→non-date). `ClockAssignCompatibility.TryClaimAssign` claims Now/Today onto Date/DateOnly/DateTime/Timestamp. `DateToDateTimeConversion` uses `TryAdvise` for widen metadata; default `TryClaimAssign` is false so it does not swallow clock claims.

Core lowering: `DomainExpressionLoweringPass.Library` → `Meaning.Lowering.TryLower` else throw; Add/Subtract try Meaning before core arithmetic (`TemporalLowering.DateAddDaysLowering` claims date-typed left only). No core `switch` on `DateOperation`/`Now` in lowering rewrite base.

### 5. TypeMappingRegistry / packs / exporters

**Mostly proven; residual in Http samples.** `TemporalTypeMaps.Apply` + vendor `*Defaults.ApplyTypeMaps` mutate session registry. Runtime `MapDomainTypeToAstNode` / nullability go `RuntimeAnalysisCache.ClrTypeName` → `TryPrimitiveType` / `IsNonNullableClrValueType`. `MinimalApiGenerator` switched to session CLR names. **`HttpFileGenerator` still switches on domain `"DateTime"`/`"Date"` strings** (not in this PR’s exporter cleanup). Core `DomainTypeMapping.ToClrTypeName` no longer names Date; unmapped `"Date"` passthrough / SQL `"varchar"` if maps absent.

### 6. Parser Date vs Order; Duration; unit fold

**Proven.** Duration pattern is core `Number`+`Identifier` + `NotFollowedBy(Colon)` (`DslGrammar.cs`). Without temporal fold, `FoldPrimary` `"duration"` errors “requires uses temporal”. Unit validity: `TemporalExpressionPrintBinders` / `DurationForm.TryGetUnit` throw `FormatException` on unknown unit (TemporalGolden fortnights). Property types: `typed-line` treats `IsKnownPrimitiveName` (session `PrimitiveSeeds`) as property (`Due: Date`); entity names without seed stay nav (`Orders: Order` via nav patterns). Covered indirectly by DomainExtension / Mcp pack tests with `Due: Date`.

### 7. OwnershipAggregatePass / PR68 F1 Last-vs-first

**Unchanged (not regressed).** Diff only drops `Dependencies => […]`. `BuildAggregate` still `GroupBy` → `g.Last()` at `OwnershipAggregatePass.cs:48`. Same Last-wins duplicate-entity policy as master; no PR72 fix or new oracle.

### 8. CORE / analysis-pass-guide honesty

**CORE updated** for registration order, ExpressionMeaning, Storage overlay, TryClaimAssign, TypeMappingRegistry. **Live ghosts remain** in interpretation docs (Issues 1–2). Archive/plans mentions of Dependencies OK per out-of-scope note.

### 9. Runtime DomainEntityInstance diffs

**Mostly contract-preserving.** Defaults go Meaning.Defaults after ident rewrite; type AST mapping via session CLR; ValidateConstraints takes `domain` for nullability. Fail-open: unknown CLR → `Prim.Structure`; null `domain` → core `ToClrTypeName` (Date → `"Date"` ≠ non-nullable list) — behavioral divergence vs old hard-coded Date non-nullable when domain omitted. Product paths pass domain + temporal extensions in dogfood/clock tests.

### 10. Tests / oracles

| Oracle | Theater? |
|--------|----------|
| `AnalyzerDependencyScheduleTests` | Real: order, overwrite, duplicate/empty throw |
| `PassDependencyDeclarationTests` | Real telemetry indices; HasNoTemporal/HasNoStorage real |
| `TemporalGoldenTests` | Real parse IR + lowering (Meaning injected) |
| `ClockLoweringTests` | Real FromDateTime + runtime DateOnly store |
| `StandardAnalyzer_PassNames_MatchInterpreterPipeline` | Real string equality to Interpreter |
| `DomainSessionTests.Analyze_TemporalLibrary_RegistersMeaningNotAPass` | Real Meaning vs pass |

## Issues

### Issue 1 -- Severity: bug
- File: `docs/interpretation/analysis-pass-guide.md:10`
- Description: Overview still says passes declare `After` / `Before` so `Build` can schedule them. That API is gone; `Build` is registration order. Ordering section (≈148) was fixed, but Overview remains false advertising against this PR’s doc-honesty claim (no ghost Before/After in live docs).
- Suggestion: Replace with registration-order wording aligned to CORE / Interpretation Analysis README.
- Status: open

### Issue 2 -- Severity: bug
- File: `docs/interpretation/README.md:55-56`
- Description: Key Concepts still say passes declare dependencies and order via topological sort. Matches deleted AnalyzerBuilder insert scheduler; contradicts `Interpreter` registration-order pipeline and README under `Poly/Interpretation/Analysis/`.
- Suggestion: Rewrite to registration order; point at Analysis README pass list.
- Status: open

### Issue 3 -- Severity: suggestion
- File: `src/Poly.DslCompiler/HttpFileGenerator.cs:199-200` (and `:218-219`)
- Description: PR claim: exporters use session CLR maps, not Date/DateTime domain lists. `MinimalApiGenerator` was updated; Http sample JSON still hard-codes domain type names `"DateTime"`/`"Date"`/…. Sibling exporter path not on the new map.
- Suggestion: Route sample values through `RuntimeAnalysisCache.ClrTypeName` (or equivalent session maps) like MinimalApi.
- Status: open

### Issue 4 -- Severity: suggestion
- File: `Poly/DomainModeling/Analysis/OwnershipAggregatePass.cs:48`
- Description: Focus item PR68 F1 Last-vs-first: still `g.Last()` for duplicate entity names. Unchanged vs master (only Dependencies line removed). Not a PR72 regression; residual ambiguity under evolution duplicates.
- Suggestion: Track as residual; prefer fail-closed on duplicate names if product wants First/Last settled.
- Status: open

### Issue 5 -- Severity: suggestion
- File: `Poly/DomainModeling/Meaning/DomainTypeMapping.cs:21-22` / `:38` (with `RuntimeAnalysisCache.ClrTypeName` null-domain branch)
- Description: Without temporal TypeMaps (or `domain == null`), `ToClrTypeName("Date")` returns `"Date"`; `ToSqlColumnType` falls through to `"varchar"`; `TryPrimitiveType` fails → runtime `Prim.Structure`. Soft/fail-open vs old core Date→DateOnly/date maps. Reachability: manual IR or Create without domain; product `uses temporal` + Open path is fine.
- Suggestion: Fail closed on unknown domain→CLR for known temporal spellings when maps missing, or document null-domain as unsupported for temporal props; add a contract test that strips TypeMaps / null domain.
- Status: open

### Issue 6 -- Severity: suggestion
- File: `Poly/DomainModeling/Libraries/Storage/PersistenceEmitLibrary.cs:15` / `src/Poly.Packs.Sqlite/SqlitePack.cs:15`
- Description: Both register `StoragePass` + `PersistenceSurfacePass` under the same PassNames. Comment says do not load both; `SessionBuilder.AddAnalyzer` fails closed on duplicate. No test forces `uses persistence` + `uses sqlite` and asserts the throw.
- Suggestion: Add fail-closed dual-load test; optionally catalog-level mutual exclusion with a clearer diagnostic.
- Status: open

### Issue 7 -- Severity: nit
- File: `docs/interpretation/analysis-pass-guide.md:15`
- Description: Lifecycle step still says “scheduled order” after Overview’s Before/After claim; soft wording drift next to Issue 1.
- Suggestion: Say “registration order” when fixing Issue 1.
- Status: open

## Scope drift

None material: tip has no SQLite wal/shm artifacts; no stale `docs/plans/` examination notes in the PR file set (archive plan text mentioning Dependencies left alone — allowed).
