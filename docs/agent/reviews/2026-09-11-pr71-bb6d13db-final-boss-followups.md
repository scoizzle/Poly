# PR 71 follow-ups (Final Boss) — 2026-09-11

- **Review**: [`2026-09-11-pr71-bb6d13db-final-boss.md`](2026-09-11-pr71-bb6d13db-final-boss.md)
- **PR**: [#71](https://github.com/scoizzle/Poly/pull/71) (SHA `bb6d13dbda7d716466dea35c6a52b724ddd4b860`)
- **Mode**: re-verify of Razor `docs/agent/reviews/2026-09-11-pr71-bb6d13db-razor.md` + follow-ups
- **Model**: grok-4.6
- **Open bugs**: none
- **Verdict**: **ship** — do not block on F1–F6. Foreman merges.

## Follow-up Tasks

- [ ] **F1** — **suggestion** — `docs/CORE.md:119`. Document diagnostics **snapshot** (`ToList` at `AnalysisResult` construction) and **HasErrors SoT** from `AnalysisContext.HasErrors` (not re-scanned from the published list). Schedule row already FailFast-honest.
- [ ] **F2** — **suggestion** — `docs/technical/syntax-analysis-framework.md:67-70`. Remove or rewrite the `OptionsUsed` / `AnalysisWasTerminatedEarly` “kept” claim — those members are absent on tip `AnalysisResult`. (Carries PR70 Razor F2; tip edited the FailFast sibling paragraph only.)
- [ ] **F3** — **suggestion** — `Poly/Analysis/AnalysisResult.cs:10`. Make `HasErrors` required (drop `= false` default) or assert consistency with `Diagnostics` so hand-built / `with` results cannot lie to `DomainEvolution` / `DomainModelAnalyzer`. MCP `DomainTools.cs:1431` still scans the list (sibling; agrees on Analyzer path).
- [ ] **F4** — **nit** — `Poly/Analysis/AnalysisOptions.cs:42`. `FailFast` ordinal `2→1` after deleting `StopOnStructuralErrors`. No in-repo integer casts; note ABI only if external persisted modes exist. Not a ship-blocker.
- [ ] **F5** — **nit** — `Poly.Tests/Syntax/Analysis/AnalyzerDiagnosticsTests.cs:47-55`. Optional stronger SoT oracle (mismatched flag vs list on the record, or spy that Analyzer passes `context.HasErrors`). FailFast skip test already adequate for early-exit.
- [ ] **F6** — **suggestion** — `docs/technical/syntax-analysis-framework.md:25`. Table still says `AnalysisOptions` is a “3-value enum”; tip enum is `Full | FailFast` only. Update in the same file this PR already edited. Not a merge bar.

## Disposition (Razor `bb6d13db`)

| Prior | Disposition on tip `bb6d13db` |
|-------|-------------------------------|
| Razor **F1** (CORE diagnostics row) | **still open** → **F1** |
| Razor **F2** (ghost OptionsUsed / AnalysisWasTerminatedEarly) | **still open** → **F2** |
| Razor **F3** (`HasErrors = false` default) | **still open** → **F3** |
| Razor **F4** (FailFast ordinal 2→1) | **still open** → **F4**; re-verified **not** a ship-blocker (`git grep '(AnalysisMode)'` empty; product Default `Full = 0`) |
| Razor **F5** (weak SoT test) | **still open** → **F5** |
| Razor “0 bugs / ship” | **stands**. No new bug. Extra suggestion **F6** (3-value enum table). |
| Razor SIMPLIFY 1 (FailFast collapse) | **proved** — `StopOnStructuralErrors` gone from `Poly/` + `Poly.Tests/` C#; `ShouldStopOnErrors == FailFast` |
| Razor SIMPLIFY 2 (ToList, no DistinctBy) | **proved** — `Analyzer.cs:39`; `git grep DistinctBy` empty under `Poly/Analysis/` |
| Razor SIMPLIFY 3 (HasErrors from context flag) | **proved** — `Analyzer.cs:42` `context.HasErrors`; no `Diagnostics.Any` under `Poly/Analysis/` |

## Disposition (older)

| Prior | Disposition on tip `bb6d13db` |
|-------|-------------------------------|
| PR70 Razor **F1** (Stop ≡ FailFast → delete one mode) | **fixed** — single `FailFast` early-stop; Stop factory/enum member gone |
| PR70 Razor **F2** (ghost OptionsUsed / AnalysisWasTerminatedEarly) | **still open** → **F2** |
| PR70 Razor **F3** (HasErrors from context flag) | **fixed** |
| PR70 Razor **F4/F5** (StructuralFailure naming) | **still open**, **scope OUT** |
| PR70 Razor **F6** (EOF newlines on AnalysisOptions/Result) | **fixed** on tip files (both end with `\n`) |
| PR69 Final Boss **F1** / Razor **F1** (ToList snapshot, no DistinctBy) | **fixed** — `context.Diagnostics.ToList()` |
| PR69 Razor **F2** (ErrorCount UX / presentation multisets) | **scope OUT** |
| PR69 Razor **F3** (stronger keep-both messages) | **still open**, out of rank |

## Scope OUT (do not treat as tip bugs)

- DistinctBy restore
- Ghost-docs beyond filing F2/F6
- ErrorCount UX
- Item5
- StructuralFailure naming restore

## Process

- [ ] **P1** — When collapsing enum members onto one survivor, either preserve the survivor’s ordinal or call out the renumber in the same change (FailFast `2→1` this tip). (Razor P1, still valid.)
- [ ] **P2** — When a tip edits a docs file for one claim (FailFast collapse), grep sibling “kept” claims **and** summary tables in that file for ghost APIs / stale cardinalities (recurring PR70→PR71; Razor caught OptionsUsed, missed “3-value enum”).
- [ ] **P3** — Rank claims that change SoT (flag vs scan, snapshot vs alias) should update the matching CORE row in the same diff — schedule was updated; diagnostics row was not. (Razor P3, still valid.)
