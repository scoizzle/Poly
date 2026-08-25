# Micro-Task: DAR.D1 - Evolution Context Removal

Parent: ../domain-authoring-context-removal-plan.md (Phase D)
Queue: ./dar-README.md
Difficulty: Medium
Status: [x] Completed 2026-07-28
Prereq: DAR.C1

## Objective

Remove `DomainEvolution` dependency on `DomainAuthoringContext` and ensure
evolution always analyzes through the single analyzer definition plus explicit
inputs.

## Tasks

- [x] D1.1 Remove `DomainEvolution(..., DomainAuthoringContext?)` constructor
      path.
- [x] D1.2 Replace internal context storage with explicit immutable inputs.
- [x] D1.3 Update evolution analyze calls (full/incremental) to use the unified
      analyzer API.

## Primary Files

- Poly/DomainModeling/Evolution/DomainEvolution.cs
- Poly/DomainModeling/Evolution/*
- Poly.Tests/DomainModeling/Evolution/*

## Acceptance Criteria

- [x] No evolution constructor or method requires `DomainAuthoringContext`.
- [x] Evolution behavior remains fail-closed on missing/invalid analysis inputs.
- [x] Existing evolution semantics remain intact under tests.

## Verification

- [x] Build green.
- [x] Evolution tests green.

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*Evolution*'
```

## Out of Scope

- MCP session model migration.

## Progress Notes

- `DomainEvolution` now stores optional `DomainAnalysisInputs`.
- Full and incremental analysis calls use the unified explicit-input analyzer path.
