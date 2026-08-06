# Micro-Task: DAR.C1 - Converge Analyzer API

Parent: ../domain-authoring-context-removal-plan.md (Phase C)
Queue: ./dar-README.md
Difficulty: Medium
Status: [x] Completed 2026-07-28
Prereq: DAR.B1

## Objective

Collapse `DomainModelAnalyzer` to one analyzer construction path with no
`DomainAuthoringContext` branch.

## Tasks

- [x] C1.1 Remove context-based pipeline builder branch.
- [x] C1.2 Keep full + incremental analysis entry points, both backed by the
      same pipeline definition.
- [x] C1.3 Route all call sites through explicit input model introduced in B1.

## Primary Files

- Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs
- Poly/DomainModeling/Analysis/StoragePass.cs
- Poly/DomainModeling/Analysis/StorageAnalyzer.cs

## Acceptance Criteria

- [x] Exactly one analyzer pipeline definition exists.
- [x] Incremental analysis uses same pass composition as full analysis.
- [x] No analyzer behavior branch remains on `DomainAuthoringContext`.

## Verification

- [x] Build green.
- [x] Analysis tests green.

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DomainModelAnalyzer*'
```

## Out of Scope

- Evolution/MCP call-chain changes.

## Progress Notes

- `DomainModelAnalyzer` now consumes `DomainAnalysisInputs`.
- Storage configuration is injected through explicit inputs only.
