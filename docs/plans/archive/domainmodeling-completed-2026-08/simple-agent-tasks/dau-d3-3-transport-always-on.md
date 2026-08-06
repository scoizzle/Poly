# Micro-Task: DAU.D3.3 — Transport always-on domain pipeline

**Suite:** [`dau-README.md`](dau-README.md) **#D3.3**  
**Parent:** [`../domain-analysis-unification.md`](../domain-analysis-unification.md) §5 Phase 3  
**Difficulty:** Medium  
**Prereq:** **D3.2** (storage registered; hierarchy metadata available on domain analyze)  
**Status:** `[x]` — Dependencies fixed, on domain pipeline

## Objective

Every domain analyze produces `TransportMetadata` (domain exposable surface). RestApi/MinimalApi remain **emit consumers** of this bag — do **not** add RestApi metadata types.

## Required Reading

1. `Poly/DomainModeling/Analysis/TransportPass.cs`  
2. `Poly/DomainModeling/Analysis/TransportMetadata.cs`  
3. `DomainModelAnalyzer.cs` pipeline registration  
4. Parent plan: RestApi is a transport **implementation**, not domain analysis  

## Exact Steps

1. Ensure Transport algorithm lives in Analysis (`TransportPass` already absorbed analyzer body in D3.0 era — verify no dual Lowering TransportAnalyzer still required for product path).
2. Set `Dependencies` to require ownership + topology Ids (same as Storage).
3. Register `new TransportPass()` on domain pipeline **after** OwnershipAggregatePass (after Storage is fine).
4. Keep fail-closed if aggregate/topology missing (already present — do not regress).
5. Test: `DomainAnalysis_ProducesTransportMetadata` — non-null `TransportMetadata` after Analyze on a small multi-entity domain.

## Definition of Done

- [ ] TransportPass registered on `UseDomainModelAnalysisPipeline` / factory path  
- [ ] `Analyze` yields `TransportMetadata` without running DslCompiler  
- [ ] No new RestApi* analysis types  
- [ ] Build + new test green  
- [ ] `dau-README` D3.3 `[x]`; CURRENT → D3.4  

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*Transport*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DomainAnalysis_Produces*'
```

## Out of Scope

- MinimalApi/HttpFile changes  
- MCP  
- Deleting Transport  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
