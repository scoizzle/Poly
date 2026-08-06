# Micro-Task: DACR.P3 - DslCompiler Semantic Lookup Migration

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Medium
Status: [x] Complete
Prereq: DACR.P1 complete

## Objective

Use analysis metadata for semantic generation decisions while preserving structural output traversal.

## Tasks

- [x] P3.1 Keep DslCompiler core generation on analysis metadata as the only semantic source.
- [x] P3.2 Replace repeated enum and relationship semantic scans in generators with metadata-backed lookups.
- [x] P3.3 Preserve direct traversal only for ordering and rendering.
- [x] P3.4 Require AnalysisResult in generator entry points where semantic decisions are made.

## Primary Files

- src/Poly.DslCompiler/DslCompiler.cs
- src/Poly.DslCompiler/MinimalApiGenerator.cs
- src/Poly.DslCompiler/HttpFileGenerator.cs
- src/Poly.DslCompiler/DbContextGenerator.cs

## Acceptance Criteria

- [ ] Semantic generator logic no longer re-derives meaning from direct scans in touched paths.
- [ ] Entry points in scope do not allow semantic generation without analysis.

## Verification

- [x] Build green.
- [x] DslCompiler tests green.
- [x] Output regression tests unchanged where behavior should match.

## Progress Notes

- [x] `MinimalApiGenerator` now requires `AnalysisResult` and uses `EntityStructureMetadata.ConstructorParameters` for create/seed constructor argument ordering (analysis-first semantics).
- [x] `HttpFileGenerator` now requires `AnalysisResult` and avoids repeated enum scans by using a precomputed enum lookup cache.
- [x] `DslCompiler` now passes `AnalysisResult` into semantic generator entry points for `Program.cs` and `demo.http` generation.
- [x] Removed residual direct relationship scans in `MinimalApiGenerator` create/action/seed paths by using `StorageEntity.CollectionNavigations` metadata from analysis.
