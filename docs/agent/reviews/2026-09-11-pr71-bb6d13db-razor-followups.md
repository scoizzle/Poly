# PR 71 follow-ups (Razor) — 2026-09-11

Tip: `bb6d13dbda7d716466dea35c6a52b724ddd4b860`  
Review: `docs/agent/reviews/2026-09-11-pr71-bb6d13db-razor.md`  
Verdict: **ship** (0 bugs)

## Open

- [ ] **F1** — **suggestion** — `docs/CORE.md:119`. Document diagnostics **snapshot** (`ToList` at `AnalysisResult` construction) and **HasErrors SoT** from `AnalysisContext.HasErrors` (not re-scanned from the published list). Schedule row already FailFast-honest.
- [ ] **F2** — **suggestion** — `docs/technical/syntax-analysis-framework.md:67-70`. Remove or rewrite the `OptionsUsed` / `AnalysisWasTerminatedEarly` “kept” claim — those members are absent on tip `AnalysisResult`. (Carries PR70 Razor F2; tip edited the FailFast sibling paragraph only.)
- [ ] **F3** — **suggestion** — `Poly/Analysis/AnalysisResult.cs:10`. Make `HasErrors` required (drop `= false` default) or assert consistency with `Diagnostics` so hand-built / `with` results cannot lie to `DomainEvolution` / `DomainModelAnalyzer`.
- [ ] **F4** — **nit** — `Poly/Analysis/AnalysisOptions.cs:42`. `FailFast` ordinal `2→1` after deleting `StopOnStructuralErrors`. No in-repo integer casts; note ABI only if external persisted modes exist.
- [ ] **F5** — **nit** — `Poly.Tests/Syntax/Analysis/AnalyzerDiagnosticsTests.cs:47-55`. Optional stronger SoT oracle (mismatched flag vs list on the record, or spy that Analyzer passes `context.HasErrors`). FailFast skip test already adequate for early-exit.

## Disposition (priors)

| Prior | Disposition on tip `bb6d13db` |
|-------|-------------------------------|
| PR70 Razor **F1** (Stop ≡ FailFast → delete one mode) | **fixed** — single `FailFast` early-stop; Stop factory/enum member gone |
| PR70 Razor **F2** (ghost OptionsUsed / AnalysisWasTerminatedEarly) | **still open** → **F2** |
| PR70 Razor **F3** (HasErrors from context flag) | **fixed** |
| PR70 Razor **F4/F5** (StructuralFailure naming) | **still open**, **scope OUT** |
| PR70 Razor **F6** (EOF newlines on AnalysisOptions/Result) | **fixed** on tip files |
| PR69 Razor **F1** (ToList snapshot, no DistinctBy) | **fixed** |
| PR69 Razor **F2** (ErrorCount UX / presentation multisets) | **scope OUT** |
| PR69 Razor **F3** (stronger keep-both messages) | **still open**, out of rank |

## Scope OUT (do not treat as tip bugs)

- DistinctBy restore  
- Ghost-docs beyond filing F2 (full ghost inventory / Last-vs-first)  
- ErrorCount UX  
- Item5  
- StructuralFailure naming restore  

## Process

- [ ] **P1** — When collapsing enum members onto one survivor, either preserve the survivor’s ordinal or call out the renumber in the same change (FailFast `2→1` this tip).
- [ ] **P2** — When a tip edits a docs file for one claim (FailFast collapse), grep sibling “kept” claims in that file for ghost APIs (recurring PR70→PR71).
- [ ] **P3** — Rank claims that change SoT (flag vs scan, snapshot vs alias) should update the matching CORE row in the same diff — schedule was updated; diagnostics row was not.
