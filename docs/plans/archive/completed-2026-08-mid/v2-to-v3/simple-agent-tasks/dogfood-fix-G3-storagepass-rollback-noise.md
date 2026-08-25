# Fix G3 — StoragePass noise on evolve rollback diagnostics

**Suite:** [`dogfood-fix-README.md`](dogfood-fix-README.md)  
**Finding:** DOGFOOD-S1-MUTATION G3 — duplicate property (etc.) rollback shows StoragePass “requires EffectTopology…” and hides the real error  
**Bucket:** A (Analysis noise)  
**Difficulty:** Small–Medium  
**Status:** `[x]` — HasStructuralFailure guard + regression test

## Objective

When evolution **fails** for a structural/authoring reason, user-facing diagnostics must lead with that reason. StoragePass missing-dependency errors must **not** dominate or confuse rollback messages when the domain was never in a state to produce full infra metadata (or when structural failure already aborted the useful path).

**Do not delete D3.0 fail-closed for intentional isolated StoragePass runs** (codegen isolation tests). Fix **presentation** and/or **when** StoragePass emits MissingDependency during full domain pipeline / evolve.

## Required Reading

1. Finding G3 in mutation report  
2. `Poly/DomainModeling/Analysis/StoragePass.cs` — fail-closed block  
3. `Poly/DomainModeling/Queries/DomainQueries.cs` — `GetAnalysisSummary` (Messages take first 10 errors+warnings)  
4. How MCP returns diagnostics on failed evolve (`DomainTools` / evolution response)

## Exact Steps

1. Reproduce: evolve add duplicate property (or similar); inspect diagnostic list order/content.  
2. Prefer one of (smallest that fixes dogfood):
   - **A (preferred):** During full pipeline when `HasStructuralFailure` is already set **before** StoragePass, StoragePass **returns without Error** (skip). Fail-closed remains when StoragePass runs in isolation with no agg/topo and no structural failure context — or when invoked with explicit priorAnalysis expectation.  
   - **B:** MCP/DomainQueries message list **prioritizes** non-`StoragePass.MissingDependency` errors first, and/or filters that code from evolve failure Message when other Errors exist.  
   - **C:** StoragePass MissingDependency severity → Warning when other Errors exist (weaker).  
3. Keep standalone test `StoragePass_FailsClosed_WithoutAggregateAndTopology` green (isolated Analyze path).  
4. Add test: full `DomainEvolution` / `DomainModelAnalyzer.Analyze` with **structural** error (e.g. duplicate property via evolve if easy, or invalid domain) → user-facing summary messages **include** the structural issue; StoragePass missing-deps either absent or not the only/first line.  

## Definition of Done

- [x] Dogfood-style structural failure surfaces the real error clearly  
- [x] D3.0 isolated StoragePass fail-closed still green  
- [x] Automated test locks the behavior  
- [x] Build + targeted tests green  
- [x] fix-README CURRENT → HOST or S1-R  

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*StoragePass*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DomainEvolution*'
```

## Out of Scope

- Removing StoragePass from domain pipeline  
- Changing codegen fail-closed when storage truly missing  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**  
