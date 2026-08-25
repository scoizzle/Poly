# Micro-Task: APM.A3 — Slim DslCompiler codegen pipeline

**Suite:** [`apm-README.md`](apm-README.md) **#A3**  
**Parent:** [`../analysis-pipeline-merge.md`](../analysis-pipeline-merge.md) §3.2  
**Difficulty:** Small  
**Estimated Context:** ~10k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** **A2**

## Objective

`DslCompiler.GenerateAllFiles` no longer runs topology/aggregate/behavior passes. It reads **Behavior** and **Aggregate** from the domain `analysis` argument; codegen pipeline runs **StoragePass** (+ Transport + packs only).

## Required Reading

- Parent §3.2  
- `src/Poly.DslCompiler/DslCompiler.cs` — `GenerateAllFiles` infra builder (~196–234)  
- Fail-closed checks for storage / behavior / aggregate  

## Exact Steps

1. Remove `EffectTopologyPass`, `OwnershipAggregatePass`, `BehaviorPass` from the inline builder.
2. Keep `StoragePass` with `analysis: analysis` (domain result).
3. Keep `TransportPass` until G6.h1 (still OK to register).
4. Keep pack `authoring.Passes.Build()` on codegen builder.
5. Extract:
   - `behaviorModel` / `aggregateModel` from **`analysis`**
   - `storageModel` from **codegen** `Analyze` result
6. Fail-closed messages may say “domain analysis” for missing behavior/aggregate if clearer.
7. Ensure `priorAnalysis: analysis` still passed into codegen `Analyze`.

## Verification

- [ ] Build green  
- [ ] Codegen path compiles; no null-ref when metadata present  
- [ ] Spot-check or leave full regression to A5  

## Output

- `DslCompiler.cs` only (ideally)  
- Status Done  

## Out of Scope

- New tests (A4/A5)  
- Deleting TransportPass  
- Changing generator IR  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
