# Micro-Task: DAU.D3.2 — Storage always-on domain pipeline

**Suite:** [`dau-README.md`](dau-README.md) **#D3.2**  
**Parent:** [`../domain-analysis-unification.md`](../domain-analysis-unification.md) §5 Phase 3  
**Difficulty:** Medium  
**Prereq:** **D3.1**  
**Status:** `[x]` — §14 Dependencies verified

## Objective

Every `DomainModelAnalyzer.Analyze` produces `StorageMappingMetadata` on the domain (core defaults when no authoring maps). Algorithm lives under **Analysis** (absorb or move from Lowering). Fail-closed rules from D3.0 remain.

## Required Reading

1. `Poly/DomainModeling/Analysis/StoragePass.cs`  
2. `Poly/DomainModeling/Lowering/StorageAnalyzer.cs` (header + ctor + `Analyze` signature only if large)  
3. `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs` — factory from D3.1  
4. `Poly/DomainModeling/Analysis/OwnershipAggregatePass.cs` / `EffectTopologyPass.cs` — Ids for Dependencies  

## Exact Steps

1. **Dependencies:** Set `StoragePass.Dependencies` to at least  
   `[EffectTopologyPass.Id, OwnershipAggregatePass.Id]`  
   (use the actual `const string Id` values on those types).
2. **Register** on the domain pipeline **after** OwnershipAggregatePass (and topology):
   - When building analyzer with `authoring == null`:  
     `new StoragePass()` (defaults).  
   - When `authoring != null`:  
     `new StoragePass(typeMaps: authoring.TypeMaps, conventions: authoring.StorageConventions)`.  
   - Optionally append `authoring.Passes.Build()` **after** Storage only if those passes declare deps that exist — if unsure, **skip PassRegistry until D3.5** and note in task Notes.
3. **Home for algorithm:** Prefer moving `StorageAnalyzer` + needed helpers into `Poly/DomainModeling/Analysis/` **or** fold body into `StoragePass` if size allows. Update namespaces/usings. Lowering must not own the primary algorithm after this task.
4. Keep `StorageMappingMetadata` as the public bag on the domain node.
5. D3.0 fail-closed: still Error + no SetMetadata if aggregate/topology missing.
6. Tests:
   - `DomainAnalysis_ProducesStorageMappingMetadata` — parse tiny domain via evolution/SQL pack, `DomainModelAnalyzer.Analyze`, assert `GetMetadata<StorageMappingMetadata>(domain)` not null and `Storage.Entities.Count > 0`.
   - Existing D3.0 fail-closed test still green.

## Definition of Done

- [ ] Domain pipeline registers StoragePass (with correct Dependencies)  
- [ ] `DomainModelAnalyzer.Analyze(simpleDomain)` yields non-null `StorageMappingMetadata`  
- [ ] Storage algorithm primary home is Analysis (not only Lowering wrapper forever)  
- [ ] Fail-closed without agg/topo still works  
- [ ] Build green; new test green; full suite if practical  
- [ ] `dau-README` D3.2 `[x]`; CURRENT → D3.3  

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DomainAnalysis_*Storage*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*StoragePass*'
```

## Out of Scope

- Transport registration (D3.3)  
- DslCompiler emit-only (D3.5) — codegen may still re-run Storage temporarily  
- MCP (D3.4)  
- GenerationAssertions (D3.6b)  

## Files expected

- `DomainModelAnalyzer.cs`  
- `StoragePass.cs`  
- `StorageAnalyzer.cs` (move/absorb)  
- `StorageModel.cs` if co-located  
- One test file  

## Review feedback (2026-07-25)

**Mostly done:** Storage on domain pipeline; `StorageAnalyzer` under Analysis; produces metadata.

**Residual:** `StoragePass.Dependencies => []` — Exact Steps required topology + ownership Ids. Registration order works today but is fragile. **Fix when touching StoragePass** (or as mini residual before D3.7): set  
`Dependencies => [EffectTopologyPass.Id, OwnershipAggregatePass.Id]`.

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:** Residual deps