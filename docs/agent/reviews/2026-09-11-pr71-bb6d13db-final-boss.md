# PR 71 Final Boss Review — 2026-09-11

- **Target**: PR 71 (https://github.com/scoizzle/Poly/pull/71), branch `cleanup/analysis-simplify-stop-snapshot-haserrors`, vs `origin/master` (`e28b3f71428effec08490a60f0ec5b82a590077c`)
- **Mode**: re-verify (not rubber-stamp). Razor (`docs/agent/reviews/2026-09-11-pr71-bb6d13db-razor.md` + follow-ups) treated as claims to prove or reject from **this SHA**. Razor files not overwritten.
- **Model**: grok-4.6
- **SHA**: `bb6d13dbda7d716466dea35c6a52b724ddd4b860` (`git rev-parse HEAD` matched before review-branch commit; product HEAD not rewritten)
- **PINNED worktree**: `/workspace/Poly-pr71-bb6d13db`
- **Diff size**: 7 files, +25/−35 vs `origin/master` (single commit `bb6d13db`)
- **Issue counts**: 0 bugs, 4 suggestions, 2 nits
- **Verdict**: **ship** — leftover SIMPLIFY 1–3 hold on this SHA; FailFast collapse, `ToList` snapshot (no DistinctBy under `Poly/Analysis/`), and `HasErrors` from `context.HasErrors` are wired at the sole `new AnalysisResult(` site. Razor F1–F5 remain open and are not merge bars. No new bug.
- **Process notes**: Do not block ship on F1–F6. Foreman merges. Optional tests this session: `AnalyzerDiagnosticsTests` 8/8 passed (includes FailFast skip, Full run-later, HasErrorsFromContextFlag). CI not watched.

## Summary

PR 71 deletes `AnalysisMode.StopOnStructuralErrors` and the `StopOnStructuralErrors` factory, leaving `Full | FailFast` with `ShouldStopOnErrors == (Mode == FailFast)`. `Analyzer.RunPasses` snapshots `context.Diagnostics.ToList()` into `AnalysisResult` (still no DistinctBy) and seeds `HasErrors` from `context.HasErrors` instead of `Diagnostics.Any(Error)`. Product `AnalyzerBuilder.Build()` / Interpreter / domain pipeline still use `AnalysisOptions.Default` (`Mode = Full`). FailFast skip-later and Full run-later oracles remain. Residuals are docs honesty (CORE diagnostics row, ghost `OptionsUsed` / `AnalysisWasTerminatedEarly`, stale “3-value enum”) plus a hand-built-result footgun (`HasErrors = false` default). None fail-open on the Analyzer-produced path.

## Checklist (protocol §9)

- [x] Diff collected: `git diff origin/master...HEAD` — 7 files only; `Poly/DomainModeling/` and `Poly.Mcp/` empty in that diff
- [x] Stance: adversarial re-verify; not implementer; no product/test edits
- [x] Producer/consumer: `ReportDiagnostic` → `_diagnostics.Add` + `HasErrors=true` → `ShouldStopOnErrors && HasErrors` break → `new AnalysisResult(..., ToList(), ..., context.HasErrors)`
- [x] Sibling-path check before severity (FailFast vs Full; flag vs list scan; sole ctor site; Interpreter / evolution / MCP / queries)
- [x] Reachability: no new throw; early-exit only under FailFast; product Default remains Full
- [x] Invariant comments: `AnalysisResult` param docs claim SoT from context — checked against Analyzer construction
- [x] Counts/baselines recomputed this session (`git show bb6d13db:PATH`, `git grep` at SHA, `git show origin/master:Poly/Analysis/AnalysisOptions.cs`)
- [x] Oracles: FailFast skip + Full run-later + keep-both retained; Stop-mode test replaced; optional run 8/8 pass
- [x] Razor files not overwritten
- [x] Review + follow-ups written under `docs/agent/reviews/`

## SIMPLIFY 1–3 disposition (must-prove, this SHA)

| # | Claim | Disposition | Evidence (path:line at `bb6d13db`) |
|---|-------|-------------|-------------------------------------|
| 1 | FailFast collapse: `StopOnStructuralErrors` gone from live `Poly/` and `Poly.Tests/`; `Full\|FailFast` only; `ShouldStopOnErrors == FailFast` | **proved** | `git grep StopOnStructuralErrors bb6d13db -- Poly/ Poly.Tests/` empty (exit 1). `git grep ShouldStopOnStructuralErrors -- '*.cs'` empty. Enum: `AnalysisOptions.cs:37` `Full = 0`, `:42` `FailFast = 1` only. `ShouldStopOnErrors` `:26` `=> Mode == AnalysisMode.FailFast`. Call site `Analyzer.cs:27`. Factory `StopOnStructuralErrors` replaced by `FailFast` `:16`. |
| 2 | `Diagnostics.ToList()` snapshot into `AnalysisResult` — **NO DistinctBy** under `Poly/Analysis/` | **proved** | `Analyzer.cs:39` `context.Diagnostics.ToList()`. `git grep DistinctBy bb6d13db -- Poly/Analysis Poly.Tests/Syntax/Analysis` empty. Sole remaining C# DistinctBy: `DomainProgramProjection.cs:120` `.DistinctBy(e => e.Name)` on bound `ContractEndpoint` names — not diagnostics. Keep-both test still `Count == 2` (`AnalyzerDiagnosticsTests.cs:11`). Master passed live `context.Diagnostics` (`git show origin/master:Poly/Analysis/Analyzer.cs` ctor arg). |
| 3 | HasErrors SoT: `AnalysisResult` takes `bool HasErrors` from `context.HasErrors` (not `Diagnostics.Any` scan) | **proved** | `AnalysisResult.cs:3` param docs; `:10` `bool HasErrors = false` (no property initializer scan). Master (`git show origin/master:Poly/Analysis/AnalysisResult.cs`) had `HasErrors { get; } = Diagnostics.Any(Error)`. Tip `Analyzer.cs:42` passes `context.HasErrors`. `git grep Diagnostics.Any bb6d13db -- Poly/Analysis` empty. |

### Claim 4 (tests + product Default)

| Check | Disposition | Evidence |
|-------|-------------|----------|
| FailFast skip-later retained | **proved** | `AnalyzerDiagnosticsTests.cs:59-71` `Analyze_WhenFailFast_AndErrorReported_SkipsLaterPasses` — `Build(AnalysisOptions.FailFast)`, `later.Calls == 0`, telemetry 1 pass, `HasErrors` |
| Full run-later retained | **proved** | `:74-86` `Analyze_WhenFullMode_AndErrorReported_RunsLaterPasses` — `Build()` (Default), `later.Calls == 1`, telemetry 2 passes |
| Product Default still Full | **proved** | `AnalysisOptions.cs:10` `Default = new()`, `:21` `Mode { get; init; } = AnalysisMode.Full`. `Analyzer.cs:20` `Options = AnalysisOptions.Default`. `AnalyzerBuilder.cs:41` `options ?? AnalysisOptions.Default`. Interpreter `_analyzer` `Interpreter.cs:19-35` `.Build()` no options. `DomainModelAnalyzer.BuildPipeline` `:34` `builder.Build()` no options. `DomainSession` `:43` uses that pipeline. Live `AnalysisOptions.FailFast` consumer: tests only (`AnalyzerDiagnosticsTests.cs:64`). |
| Stop-mode skip test | **removed, not oracle-weakened** | Master `Analyze_WhenStopOnStructuralErrors_AndErrorReported_SkipsLaterPasses` gone with the mode. Replacement `:47-55` is HasErrors-from-flag (Default Full, one pass). Early-exit oracle is the retained FailFast test. |

### Claim 5 (sibling ctor + ordinal)

| Check | Disposition | Evidence |
|-------|-------------|----------|
| Sole `new AnalysisResult(` passes `HasErrors` | **proved** | `git grep 'new AnalysisResult' bb6d13db -- '*.cs'` → **one** hit: `Poly/Analysis/Analyzer.cs:36`. Args include `context.Diagnostics.ToList()` and `context.HasErrors` (`:39`, `:42`). No `with { }` on `AnalysisResult` in `Poly/`. |
| FailFast ordinal `2→1` | **nit, not ship-blocker** | Master `FailFast = 2` / `StopOnStructuralErrors = 1` (`git show origin/master:Poly/Analysis/AnalysisOptions.cs`). Tip `FailFast = 1`. `git grep '(AnalysisMode)'` / JSON of `AnalysisMode` empty under `*.cs` `*.json`. All live usages are named members. `Full = 0` unchanged. Out-of-tree persisted integer `2` would now be an unnamed value and would **not** fail-fast — residual ABI only. |

## Call graph (this SHA)

```
INodeAnalyzer.Analyze(context, root)
  → ReportError/Warning (Diagnostic.cs:47-66)
      → AnalysisContext.ReportDiagnostic (AnalysisContext.cs:75-88)
          → NormalizeSeverity / ShouldInclude
          → _diagnostics.Add
          → if severity == Error: HasErrors = true   // write-side flag; never reset

Analyzer.RunPasses (Analyzer.cs:22-43)
  → foreach pass:
        if Options.ShouldStopOnErrors && context.HasErrors: break
            // ShouldStopOnErrors == (Mode == FailFast) only
        analyzer.Analyze; RecordPass
  → new AnalysisResult(
        new NodeMetadataStore(context.Metadata),  // clone
        telemetry,
        context.Diagnostics.ToList(),             // snapshot copy
        context.Settings,
        Options,
        context.HasErrors)                        // SoT flag, not list scan
```

`AnalysisContext.Diagnostics` (`:52`) is still the live `_diagnostics` list. After this tip, `AnalysisResult.Diagnostics` is a `ToList()` copy. Mutations to context after return cannot affect the published result. Product `Analyze` drops the context.

## Sibling-path check

| Semantic | Paths | Holds on this SHA? | Test forces this sibling? |
|----------|-------|--------------------|---------------------------|
| Early-stop | `Analyzer.cs:27` `ShouldStopOnErrors && context.HasErrors`; `ShouldStopOnErrors` is FailFast only (`AnalysisOptions.cs:26`) | Yes — Stop mode gone; no second early-stop helper | **Yes** FailFast skip (`:59-71` later.Calls==0). **Yes** Full continues (`:74-86` later.Calls==1). Product Default Full never takes the break. |
| HasErrors SoT on result | `AnalysisResult.HasErrors` ctor arg from `context.HasErrors` (`Analyzer.cs:42`) | Yes on the sole construction site | Weak: `:47-55` asserts both flag and `Diagnostics.Any(Error)` on the happy ReportError path (would fail if Analyzer omitted the arg; would still pass if Analyzer passed `Diagnostics.Any` instead of the flag) → F5 |
| HasErrors write-side on context | `AnalysisContext.cs:70,86-87` set on Error after include; `ReportError_SetsHasErrors` / `ReportWarning_DoesNotSetHasErrors` | Unchanged vs master; TreatWarningsAsErrors normalizes before both Add and flag (`AnalysisDiagnosticConfiguration.cs:16-19`) | Yes (`:32-44`) |
| Fail-closed compile | `Interpreter.FailLoudOnAnalysisErrors` (`Interpreter.cs:77-81`) scans `analysis.Diagnostics` for Error, does **not** read `HasErrors` | Independent sibling. On Analyzer-produced results, list and flag agree (flag set iff Error added; ToList copies that list). Compile uses `_analyzer.Analyze` then this scan (`:54-55`, `:68-69`). Hand-built skew cannot arise on this door. | Presence-of-Error compile tests exist elsewhere; not a uniqueness/SoT-independence test |
| Evolution reject | `DomainEvolution.cs:47` `analysis.HasErrors` (flag) | Consumes SoT. Trace `ErrorCount` still `Diagnostics.Count(Error)` (`:110-111`) — count sibling, agrees on Analyzer path | Evolution tests assert `result.Analysis.HasErrors` |
| RequireCatalog | `DomainModelAnalyzer.cs:61` `if (analysis.HasErrors) return;` | Consumes SoT; missing catalog still throws when flag is false | Pre-existing |
| MCP snapshot `hasErrors` | `DomainTools.cs:1431` `analysis?.Diagnostics.Any(Error)` — **list scan**, not `analysis.HasErrors` | Equivalent on Analyzer-produced results. Skew only if a hand-built `AnalysisResult` lies (F3). Not introduced by this tip; MCP file not in the 7-file delta. | No mismatch test |
| Query summary | `DomainQueries.cs:260-262` filters Diagnostics by Error for counts | Presentation; not HasErrors SoT | N/A |
| DistinctBy | `Poly/Analysis/` none; projection `DomainProgramProjection.cs:120` endpoint names | Diagnostic publish is a multiset | `KeepsBothInReportOrder` Count==2 |

**Reachability:** no new throw. FailFast break is reachable only when caller passes `AnalysisOptions.FailFast` / `Mode = FailFast`. Product Interpreter and domain pipeline do not. Severity of ordinal `2→1` is nit because no in-repo integer/JSON consumer exists.

## Docs honesty (this rank)

| Doc | Honesty |
|-----|---------|
| `docs/CORE.md:117` Schedule | FailFast-only early-stop — **matches** `Analyzer.cs:27` + `AnalysisOptions.cs:26` |
| `docs/CORE.md:119` Diagnostics | Report order + no de-duplication — **matches** ToList/multiset; **omits** snapshot copy and HasErrors-from-flag → Issue 1 |
| `docs/ARCHITECTURE.md:155` | `Full`, `FailFast` — **matches** |
| `docs/ARCHITECTURE.md:149` | Lists `HasErrors` on AnalysisResult — true; does not say SoT |
| `syntax-analysis-framework.md:72-75` FailFast | Collapse note accurate (`ShouldStopOnErrors && context.HasErrors`; one early-stop mode) |
| Same file `:67-70` | Still claims `OptionsUsed` / `AnalysisWasTerminatedEarly` “kept” — **false**; members absent (`git grep OptionsUsed/AnalysisWasTerminatedEarly -- '*.cs'` empty). Tip `AnalysisResult` has `Options` param, no terminated-early flag → Issue 2 |
| Same file `:25` | Table still says AnalysisOptions is a **3-value enum** — **false** after this collapse (`Full`, `FailFast` only). Tip edited the FailFast subsection in this file and left the table → Issue 6 (Razor missed) |

## Scope OUT (prove untouched)

| Out of scope | Proof on tip |
|--------------|--------------|
| DistinctBy restore | Not in `Poly/Analysis/`; projection DistinctBy unchanged |
| Ghost-docs fix | Still present at `syntax-analysis-framework.md:67-70` — filed Issue 2 |
| Last-vs-first / ErrorCount UX / Item5 / StructuralFailure naming | Not in 7-file delta (`git diff origin/master...HEAD -- Poly/DomainModeling/ Poly.Mcp/` empty) |

## Optional verification (read-only)

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false -- --treenode-filter "/*/*/AnalyzerDiagnosticsTests/*"
```

Passed: 8 succeeded / 0 failed (includes `Analyze_WhenFailFast_AndErrorReported_SkipsLaterPasses`, `Analyze_WhenFullMode_AndErrorReported_RunsLaterPasses`, `Analyze_WhenErrorReported_ResultHasErrorsFromContextFlag`). First `dotnet run` without `-p:NuGetAudit=false` hit NU1903 (repo policy; not a product defect). CI not watched.

## Issues

### Issue 1 -- Severity: suggestion
- File: `docs/CORE.md:119`
- Description: Rank claims include diagnostics snapshot + HasErrors SoT. Schedule row (`:117`) was updated for FailFast; diagnostics row still only says report-order / no de-duplication. Readers can miss that `AnalysisResult.Diagnostics` is a `ToList` copy and `HasErrors` is not re-derived from that list.
- Suggestion: Extend the diagnostics row: snapshot via `ToList` at result construction; `HasErrors` taken from `AnalysisContext.HasErrors` (write-side flag), not a list scan.
- Status: open (Razor Issue 1 / F1 re-verified)

### Issue 2 -- Severity: suggestion
- File: `docs/technical/syntax-analysis-framework.md:67-70`
- Description: “Reviewed and Kept” still advertises `AnalysisResult.OptionsUsed` / `AnalysisWasTerminatedEarly` as public contract. Tip `AnalysisResult` has neither (`git grep` empty under `*.cs`). This tip edited the sibling FailFast paragraph (`:72-75`) without fixing the ghost claim.
- Suggestion: Delete or rewrite that subsection to match tip (`Options` ctor param exists; no terminated-early flag).
- Status: open (Razor Issue 2 / F2 re-verified; carries PR70 Razor F2)

### Issue 3 -- Severity: suggestion
- File: `Poly/Analysis/AnalysisResult.cs:10`
- Description: `bool HasErrors = false` is an optional positional default. The sole production construction site passes `context.HasErrors` correctly, but `new AnalysisResult(..., diagnosticsWithErrors, ..., /* HasErrors omitted */)` or a `with` that drops the flag yields `HasErrors == false` while `Diagnostics` contains Errors. `DomainEvolution` (`:47`) and `DomainModelAnalyzer.RequireCatalog` (`:61`) trust `.HasErrors`. SoT is correct on the Analyzer path; the default is a footgun for hand-built results. MCP `DomainTools.cs:1431` still scans the list, so a lying flag would also split evolution vs MCP.
- Suggestion: Make `HasErrors` required (no default), or add a debug assert that `HasErrors == Diagnostics.Any(Error)` when both are supplied.
- Status: open (Razor Issue 3 / F3 re-verified)

### Issue 4 -- Severity: nit
- File: `Poly/Analysis/AnalysisOptions.cs:42`
- Description: `FailFast` ordinal changed from `2` (master) to `1` (tip) because `StopOnStructuralErrors = 1` was deleted. No in-repo integer cast or serialization of `AnalysisMode` found; all live usages are named members; product Default `Full = 0` unchanged. Residual ABI risk only for out-of-tree persisted integers (former `2` would no longer fail-fast).
- Suggestion: Note the ordinal break if external consumers exist; otherwise accept as C# enum cleanup. Not a ship-blocker.
- Status: open (Razor Issue 4 / F4 re-verified; not elevated)

### Issue 5 -- Severity: nit
- File: `Poly.Tests/Syntax/Analysis/AnalyzerDiagnosticsTests.cs:47-55`
- Description: `Analyze_WhenErrorReported_ResultHasErrorsFromContextFlag` asserts both `result.HasErrors` and `Diagnostics.Any(Error)` — both true on the normal ReportError path, so it does not prove independence from a list scan (Analyzer could pass `Diagnostics.Any` and still pass). It **does** prove the ctor arg is not omitted (default `false` would fail). FailFast skip oracle remains strong separately. Not test theater for early-exit; weak for SoT-vs-scan specifically.
- Suggestion: Optional: construct `AnalysisResult` directly with mismatched flag/list, or spy that Analyzer passes `context.HasErrors`.
- Status: open (Razor Issue 5 / F5 re-verified)

### Issue 6 -- Severity: suggestion
- File: `docs/technical/syntax-analysis-framework.md:25`
- Description: Key-types table still calls `AnalysisOptions` a “3-value enum”. This SHA collapsed the enum to `Full | FailFast` (`AnalysisOptions.cs:32-43`) and updated the FailFast “Why keep” note in the same file. The table is now a lie. (Line counts / `Poly/Syntax/Analysis/` path in that doc are older staleness; this row is the one the collapse made newly false.)
- Suggestion: Change to a two-value enum (`Full`, `FailFast`) in the same change that deletes the third member.
- Status: open (new this re-verify; Razor missed)

## Disposition (priors)

| Prior | Disposition on tip `bb6d13db` |
|-------|-------------------------------|
| Razor PR71 **F1** (CORE diagnostics row omit snapshot/SoT) | **still open** → Issue 1 |
| Razor PR71 **F2** (ghost OptionsUsed / AnalysisWasTerminatedEarly) | **still open** → Issue 2 |
| Razor PR71 **F3** (`HasErrors = false` default footgun) | **still open** → Issue 3 |
| Razor PR71 **F4** (FailFast ordinal 2→1) | **still open**, **nit** — not a ship-blocker → Issue 4 |
| Razor PR71 **F5** (weak SoT-vs-scan test) | **still open**, **nit** → Issue 5 |
| Razor “0 bugs / ship” | **stands**. No new bug. Additional suggestion: 3-value enum table (Issue 6 / F6). |
| PR70 Razor **F1** (Stop ≡ FailFast / delete one mode) | **fixed** — Stop mode + factory removed |
| PR70 Razor **F2** (ghost OptionsUsed / AnalysisWasTerminatedEarly) | **still open** → Issue 2 |
| PR70 Razor **F3** (seed HasErrors from context) | **fixed** — ctor arg from `context.HasErrors` |
| PR69 Final Boss **F1** / Razor **F1** (ToList snapshot without DistinctBy) | **fixed** — `context.Diagnostics.ToList()` |
| PR69 Razor **F2** (ErrorCount UX / presentation multisets) | **scope OUT** |
| PR69 Razor **F3** (stronger keep-both messages) | **still open**, out of rank |
