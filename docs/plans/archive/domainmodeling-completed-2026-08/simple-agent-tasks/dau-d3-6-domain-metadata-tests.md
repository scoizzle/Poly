# Micro-Task: DAU.D3.6 — Domain metadata + pack variance + AllMode regression

**Suite:** [`dau-README.md`](dau-README.md) **#D3.6**  
**Parent:** [`../domain-analysis-unification.md`](../domain-analysis-unification.md) §5 Phase 3  
**Difficulty:** Medium  
**Prereq:** **D3.2**, **D3.3**, **D3.5**  
**Status:** `[~]` **REOPENED §15** — Transport golden OK; pack-variance test weak (asserts same, not different)

## Objective

Prove domain analyze carries Storage + Transport; pack maps still change storage; codegen AllMode still green after emit-first.

## Required Reading

1. `Poly.Tests/DomainModeling/Analysis/PipelineMergeMetadataTests.cs` — style for domain analyze tests  
2. Existing Sqlite vs generic pack tests under `Poly.Tests/DomainModeling/Lowering/`  

## Exact Steps

1. Ensure tests exist (add if missing from D3.2/D3.3):
   - Domain analyze → StorageMappingMetadata  
   - Domain analyze → TransportMetadata  
2. Pack variance: same domain, `DomainAuthoringContext` with different type maps (reuse Sqlite vs generic setup from existing tests) → Storage column types or SQL type names differ when analyzed with context (D3.1/D3.4 path).
3. Run full focused regression:
   - PipelineMerge / DomainAnalysis_*  
   - AllMode  
   - SqlitePack / generic pack tests still green  
4. Fix only breakages caused by D3 — no drive-by refactors.

## Definition of Done

- [x] Automated tests cover domain Storage + Transport metadata presence  
- [ ] At least one pack-variance assertion via Analyze+authoring — **not same counts**; assert **differing** SQL/column types under two type-map configs (§15)  
- [x] AllMode + pack suites green (when last run)  
- [ ] `dau-README` D3.6 `[x]` only after pack-variance fixed

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DomainAnalysis_*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DslCompiler_AllMode*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*Sqlite*'
```

## Out of Scope

- GenerationAssertions rewrite (D3.6b)  
- Docs (D4.2)  

## Review feedback (2026-07-25) — why reopened

Missing Explicit Steps proof:

- No `DomainAnalysis_ProducesStorageMappingMetadata` / `ProducesTransportMetadata` style tests found under Analysis tests.  
- `DomainModelAnalyzerContextTests` only covers null/authoring throw paths — not metadata bags.  
- Pack variance via **Analyze+authoring** (not only DslCompiler pack tests) not clearly added.

**Required:** Add the Exact Steps goldens; then check DoD.

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:** §15 — Transport OK; pack-variance still weak

## §14 residual checklist (historical)


- [ ] `DomainAnalysis_ProducesTransportMetadata` (or equivalent) after `DomainModelAnalyzer.Analyze`
- [ ] Pack variance: same domain, two `DomainAuthoringContext` configs (or sql pack vs default) → storage differs when analyzed with authoring
- [ ] (Optional) MCP `GetDomainAnalysis` returns non-null `rootEntityNames` / `hasStorageMapping` after create session + add entity hierarchy
- [ ] Fix stale comment on `DomainAnalysis_HasInfraMetadata_CodegenProducesStorage` (domain pipeline now includes Storage)
- [ ] Check all Definition of Done boxes only after green

## §15 re-review (2026-07-25)

**Status remains residual.** Added tests help:

- ✅ `Analyze_ProducesTransportMetadata`
- ✅ `Analyze_ProducesStorageMappingMetadata`
- ⚠️ `Analyze_WithSqlPack_ProducesSameStorageAsWithout` — name and body assert **sameness**, not pack **variance**. CreateWithSqlPack does not install dialect type maps that change column SQL types.

**DoD residual:**

1. Add a test that two different authoring type-map setups produce **different** storage type/column representation under `DomainModelAnalyzer.Analyze(domain, authoring)` (reuse Sqlite vs generic registries from pack tests if possible).  
2. Or rename + document that SqlPack annotations-only path is intentionally identical — only acceptable if Exact Steps re-scoped in parent plan (prefer real variance).  
3. Check DoD boxes only after green.

