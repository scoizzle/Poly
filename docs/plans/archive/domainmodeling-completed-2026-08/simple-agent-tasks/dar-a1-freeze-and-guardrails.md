# Micro-Task: DAR.A1 - Freeze and Guardrails

Parent: ../domain-authoring-context-removal-plan.md (Phase A)
Queue: ./dar-README.md
Difficulty: Medium
Status: [x] Completed 2026-07-28
Prereq: None

## Objective

Freeze new `DomainAuthoringContext` analyzer usage and add tests that enforce
one analyzer pipeline definition across entry points.

## Tasks

- [x] A1.1 Mark context-based analyzer/evolution overloads obsolete with
      migration guidance.
- [x] A1.2 Add tests asserting identical pass composition for all analyzer
      entry points.
- [x] A1.3 Add fail-closed guard test preventing reintroduction of
      context-dependent pipeline branches.

## Primary Files

- Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs
- Poly/DomainModeling/Evolution/DomainEvolution.cs
- Poly.Tests/DomainModeling/Analysis/*

## Acceptance Criteria

- [x] New context-bearing entry points are deprecated and direct users toward
      explicit inputs.
- [x] Analyzer pass-list identity is validated by tests.
- [x] No behavior change yet for existing consumers beyond warning surface.

## Verification

- [x] Build green.
- [x] Domain analysis test slice green.

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DomainModeling*Analysis*'
```

## Out of Scope

- Introducing new input model types.
- MCP or DslCompiler migrations.

## Progress Notes

- Analyzer/evolution entry points no longer take `DomainAuthoringContext`.
- Core behavior is now driven by explicit input objects.
