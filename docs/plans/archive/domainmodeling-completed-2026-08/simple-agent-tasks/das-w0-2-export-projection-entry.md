# DAS W0.2 — Export-time projection entrypoint

**Wave:** W0 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) §5.5, §7.2  
**Difficulty:** Medium  
**Status:** `[x]`  
**Prereq:** W0.1 (same PR preferred)  

## Objective

Entity/program emit consumes a **finished** `AnalysisResult` via `DomainProgramProjection.ToSyntax` / `DomainToCSharpExporter.Export`. Projection failure fails **loud**. No silent skip of all entity files when metadata is null.

## Tasks

- [x] W0.2.1 Change `DslCompiler.GenerateAllFiles` (and any MCP export that depended on `EntitySyntaxMetadata`) to:

  ```csharp
  var types = DomainProgramProjection.ToSyntax(domain, analysis);
  // slice/generate from types
  ```

- [x] W0.2.2 Remove soft-null branch that skips entity generation when `EntitySyntaxMetadata` is absent (or treat absence as hard error only if dual-path still exists briefly).
- [x] W0.2.3 Ensure projection exceptions surface as compile/tool errors, not swallowed warnings from a pass.
- [x] W0.2.4 Add or update a regression test: analyze + export produces entity types for a minimal domain (library or fixture).
- [x] W0.2.5 Update CORE / future-state pointers if wording still says EntitySyntaxPass is the emit path.

## Primary files

- `src/Poly.DslCompiler/DslCompiler.cs`
- `Poly/DomainModeling/Lowering/DomainProgramProjection.cs`
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs`
- Emit-related tests under `Poly.Tests` / DslCompiler tests

## Acceptance criteria

- [x] Entity emit works without mid-pipeline `EntitySyntaxMetadata`.
- [x] Failed projection fails the export path loudly.
- [x] Build + relevant generation tests green.

## Progress notes

- **Implement + verify (pass, severity nit):** `GenerateAllFiles` calls `DomainProgramProjection.ToSyntax(domain, analysis)` then slices per-entity (+ Stage) defs; empty/missing slice throws `InvalidOperationException`; `Compile` wraps generation `Exception` as `Fail("Code generation failed: …")` — no `EntitySyntaxMetadata` soft-null skip.
- Zero CS references to `EntitySyntaxPass` / `EntitySyntaxMetadata` types; pipeline registration comment is export-time only; `PipelineMergeMetadataTests` has no mid-pipeline IR bag requirement.
- Sibling MCP `export_domain_to_csharp` uses `DomainToCSharpExporter.Export` → `ToSyntax` with catch→tool fail (both emit paths converge on finished analysis + projection).
- Regression: `DslCompiler_EntitiesMode_EmitsEntityTypesFromProjection` (+ `Item.cs` under Db mode); `Export_Produces_*` cover library `Export` path.
- CORE export-boundary + future-state §5.5 / success #3 already correct — no further CORE/future-state churn for this task.
- **Residual (nit, not blocking):** no dedicated fail-loud *negative* test for projection failure; inventory/docs outside this task may still mention EntitySyntax (cleanup opportunistic / later wave).
- Build/tests: implement session green on DslCompiler / Export / DomainAnalysis paths; verifier session was read-only structural AC + present tests (suite not re-executed).
