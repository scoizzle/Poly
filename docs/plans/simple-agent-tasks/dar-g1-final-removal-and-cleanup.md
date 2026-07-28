# Micro-Task: DAR.G1 - Final Removal and Cleanup

Parent: ../domain-authoring-context-removal-plan.md (Phase G)
Queue: ./dar-README.md
Difficulty: Medium
Status: [x] Completed 2026-07-28
Prereq: DAR.F1

## Objective

Delete `DomainAuthoringContext` and remove compatibility shims once all call
chains have been migrated.

## Tasks

- [x] G1.1 Delete `Poly/DomainModeling/DomainAuthoringContext.cs`.
- [x] G1.2 Remove deprecated overloads and compatibility shims introduced in
      earlier phases.
- [x] G1.3 Replace/remove tests that instantiate `DomainAuthoringContext`.
- [x] G1.4 Update docs and migration notes to reflect final state.

## Primary Files

- Poly/DomainModeling/DomainAuthoringContext.cs
- Poly/DomainModeling/Analysis/*
- Poly/DomainModeling/Evolution/*
- Poly.Mcp/Sessions/*
- src/Poly.DslCompiler/*
- Poly.Tests/**/*

## Acceptance Criteria

- [x] No production source references to `DomainAuthoringContext` remain.
- [x] Public contracts no longer expose context-based analysis/evolution APIs.
- [x] Migration path/docs reflect the final explicit-input model.

## Verification

- [x] Build green.
- [x] Full test suite green.
- [x] Search confirms no remaining production references.

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
rg -n "DomainAuthoringContext" Poly Poly.Mcp src/Poly.DslCompiler src/Poly.Packs.*
```

## Out of Scope

- Grammar/token-stream generalization implementation (tracked in
  `docs/plans/grammar-integration.md`).

## Progress Notes

- Removed `DomainAuthoringContext` and migrated all production/test call sites.
- Verified `rg -n "DomainAuthoringContext" Poly Poly.Mcp src` returns no matches.
