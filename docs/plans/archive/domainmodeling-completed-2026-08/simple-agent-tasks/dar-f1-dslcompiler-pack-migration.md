# Micro-Task: DAR.F1 - DslCompiler and Pack Migration

Parent: ../domain-authoring-context-removal-plan.md (Phase F)
Queue: ./dar-README.md
Difficulty: Large
Status: [x] Completed 2026-07-28
Prereq: DAR.E1

## Objective

Migrate DslCompiler and DBMS pack defaults from mutable authoring-context
mutation to explicit immutable input/descriptors.

## Tasks

- [x] F1.1 Replace `CreateAuthoring(DbmsPack)` with explicit analysis/parser
      input creation.
- [x] F1.2 Refactor pack defaults APIs to produce immutable descriptors instead
      of mutating context.
- [x] F1.3 Keep command/CLI pack surface (for example `DbmsPack`) while mapping
      internally to explicit inputs.
- [x] F1.4 Add regression tests for Sqlite/SqlServer/MySql output parity.

## Primary Files

- src/Poly.DslCompiler/DslCompiler.cs
- src/Poly.Packs.Sqlite/*
- src/Poly.Packs.SqlServer/*
- src/Poly.Packs.MySql/*
- Poly.Tests/DomainModeling/*

## Acceptance Criteria

- [x] No pack path mutates a shared `DomainAuthoringContext` instance.
- [x] Pack-driven output variance is preserved via explicit immutable inputs.
- [x] Existing consumer-facing pack-selection surface remains compatible.

## Verification

- [x] Build green.
- [x] DslCompiler + pack variance tests green.

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DslCompiler*|/*/*/*/*Pack*'
```

## Out of Scope

- Final hard delete of `DomainAuthoringContext`.

## Progress Notes

- Added `CreateInputs(DbmsPack)` in DslCompiler and migrated compile path to `DomainInputSet`.
- Pack defaults now configure `DomainInputBuilder` directly.
