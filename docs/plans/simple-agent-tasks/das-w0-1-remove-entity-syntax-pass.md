# DAS W0.1 — Remove EntitySyntaxPass from the analysis pipeline

**Wave:** W0 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) §5.5, §6, W0  
**Difficulty:** Small  
**Status:** `[x]`  
**Prereq:** none  

## Objective

Stop treating host Syntax IR as an analysis fact. Unregister `EntitySyntaxPass` from the core domain analysis pipeline so analyze no longer projects entities mid-pipeline (or soft-fails and omits metadata).

## Tasks

- [x] W0.1.1 Remove `builder.AddAnalyzer(new EntitySyntaxPass())` from `UseDomainModelAnalysisPipeline` (or equivalent).
- [x] W0.1.2 Leave type/file in place only if W0.2 still needs a temporary call site; otherwise delete pass + `EntitySyntaxMetadata` in W0.2/W0.3 follow-through.
- [x] W0.1.3 Update any test that asserts `EntitySyntaxMetadata` is non-null **after analyze alone** (e.g. `PipelineMergeMetadataTests`) — analysis-complete must not require projection bags.
- [x] W0.1.4 Note in progress: DslCompiler may break until W0.2 — acceptable if same PR/wave lands W0.2 immediately.

## Primary files

- `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs`
- `Poly/DomainModeling/Analysis/EntitySyntaxPass.cs`
- `Poly/DomainModeling/Analysis/EntitySyntaxMetadata.cs`
- `Poly.Tests/DomainModeling/Analysis/PipelineMergeMetadataTests.cs`

## Acceptance criteria

- [x] Core pipeline registration does not include EntitySyntaxPass.
- [x] `DomainModelAnalyzer.Analyze` no longer sets `EntitySyntaxMetadata` as part of default analyze.
- [x] Tests updated; build green for analysis unit tests in scope.

## Progress notes

- **Implement + verify pass** (nit). Inspected `DomainModelAnalyzer.UseDomainModelAnalysisPipeline`: 19 `AddAnalyzer` registrations, `AuthoringSuggestionAnalyzer` last; comment that Entity Syntax projection is export-time only (DAS W0); **no** `EntitySyntaxPass` registration.
- Repo-wide `.cs`: only `EntitySyntaxPass.SetMetadata` writes `EntitySyntaxMetadata`; `DslCompiler` still soft-reads the bag (W0.1.4 / W0.2 cutover).
- `PipelineMergeMetadataTests` asserts `EntitySyntaxMetadata` is **null** after `DomainModelAnalyzer.Analyze` alone.
- `EntitySyntaxPass` / `EntitySyntaxMetadata` types retained for W0.2 export-time projection.
- CORE §3.1 already export-boundary wording; future-state §5.5 has no mid-pipeline `EntitySyntaxPass`. No illegal new dual fact path.
- DslCompiler entity emit path will soft-skip / lack types until W0.2 wires export-time projection.
