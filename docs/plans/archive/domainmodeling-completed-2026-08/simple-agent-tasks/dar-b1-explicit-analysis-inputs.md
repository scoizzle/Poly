# Micro-Task: DAR.B1 - Explicit Analysis Inputs

Parent: ../domain-authoring-context-removal-plan.md (Phase B)
Queue: ./dar-README.md
Difficulty: Large
Status: [x] Completed 2026-07-28
Prereq: DAR.A1

## Objective

Introduce immutable explicit analyzer/parser input models and pack extension
descriptors so behavior no longer depends on mutable context state.

## Tasks

- [x] B1.1 Define `DomainAnalysisInputs` (or equivalent immutable type) for
      analyzer-relevant knobs.
- [x] B1.2 Define immutable `PackExtensionSet` descriptors for parser/printer
      extensions, custom node hooks, and pass contributions.
- [x] B1.3 Add validation for duplicate/invalid pass ordering and fail closed
      on invalid extension graphs.
- [x] B1.4 Ensure inputs are serializable and can be carried in session state snapshots.

## Primary Files

- Poly/DomainModeling/Analysis/*
- Poly/DomainModeling/* (input model location)
- Poly.Tests/DomainModeling/Analysis/*

## Acceptance Criteria

- [x] Analyzer-relevant options are represented as immutable explicit inputs.
- [x] Pack extensions are explicit builder-driven descriptors, not mutating context callbacks.
- [x] Invalid extension graphs (duplicate pass names) produce deterministic errors.
- [x] No hidden mutable state needed to analyze a domain.

## Verification

- [x] Build green.
- [x] New input-model and validation tests green.

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DomainModeling*'
```

## Out of Scope

- Removing old analyzer API branches (DAR.C1).

## Progress Notes

- Added `DomainInputBuilder`, `DomainInputSet`, `DomainParserInputs`, and `DomainAnalysisInputs`.
- Analyzer input validation now fails closed on duplicate additional pass names.
