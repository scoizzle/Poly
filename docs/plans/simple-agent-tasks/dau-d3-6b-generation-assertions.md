# Micro-Task: DAU.D3.6b — Retarget GenerationAssertions / IR helpers

**Suite:** [`dau-README.md`](dau-README.md) **#D3.6b**  
**Parent:** [`../domain-analysis-unification.md`](../domain-analysis-unification.md) §12  
**Difficulty:** Medium  
**Prereq:** **D3.2** (domain analyze produces storage). Prefer **D3.5**.  
**Status:** `[x]` — completed 2026-07-25

## Objective

Product-shaped IR tests go through **domain analysis metadata** (or full DslCompiler), not `new StorageAnalyzer(domain)` / null-context aggregate builds as the primary path.

## Required Reading

1. `Poly.Tests/TestHelpers/GenerationAssertions.cs` — full file  
2. Call sites of `GenerationAssertions` / direct `StorageAnalyzer` in tests (`rg StorageAnalyzer Poly.Tests`)  

## Exact Steps

1. Change helpers that build storage/behavior/aggregate for **codegen-shaped** tests to:
   - `var analysis = DomainModelAnalyzer.Analyze(domain);` (optionally with authoring context for pack tests)
   - Read `StorageMappingMetadata`, `BehaviorMetadata`, `OwnershipAggregateMetadata` from analysis  
2. If a unit test intentionally isolates StorageAnalyzer algorithm with custom maps only, keep a **narrow** internal helper named for that (e.g. `StorageAnalyzerForUnitTest`) — do not use it from DbContext/MinimalApi product tests.
3. Fix compile breaks in DbContextGeneratorTests / MinimalApiGeneratorTests / pack tests.
4. Do not change generator production code unless required by API moves.

## Definition of Done

- [ ] No product IR test path uses `new StorageAnalyzer(domain).Analyze()` without domain pipeline when full hierarchy matters  
- [ ] GenerationAssertions (or successor) documents the domain-analyze path  
- [ ] Build green; DbContext + MinimalApi + AllMode tests green  
- [ ] `dau-README` D3.6b `[x]`; CURRENT → D3.7  

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DbContext*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*MinimalApi*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DslCompiler_AllMode*'
```

## Out of Scope

- Full suite rename of every historical unit test  
- D4 docs  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
