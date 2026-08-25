# Micro-Task: APM.A2 — Register topology/aggregate/behavior on domain pipeline

**Suite:** [`apm-README.md`](apm-README.md) **#A2**  
**Parent:** [`../analysis-pipeline-merge.md`](../analysis-pipeline-merge.md) §3.1  
**Difficulty:** Small  
**Estimated Context:** ~8k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** **A1** (bridge green)

## Objective

Register `EffectTopologyPass`, `OwnershipAggregatePass`, and `BehaviorPass` in `UseDomainModelAnalysisPipeline()` with correct **Dependencies**, after producers they consume.

## Required Reading

- Parent §3.1  
- `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs` — `UseDomainModelAnalysisPipeline`  
- Pass `Id` constants: `EffectTopologyPass`, `OwnershipAggregatePass`, `BehaviorPass`, `EntityStructureAnalyzer`, `SemanticDomainAnalyzer`, `CapabilityAnalyzer`

## Exact Steps

1. Set `Dependencies` on OwnershipAggregate + Behavior as in parent §3.1 (use real `Id` strings).
2. Leave `EffectTopologyPass.Dependencies` empty (or only structural if required).
3. Register the three passes **after** `EntityStructureAnalyzer` / subscription block, **before** `AuthoringSuggestionAnalyzer` + `EntitySyntaxPass` (or rely on dependency sort — still place them intentionally).
4. Prefer parameterless pass ctors that use context (from A1).
5. Do not remove them from DslCompiler yet (A3).

## Verification

- [ ] Build green  
- [ ] `DomainModelAnalyzer.Analyze` still succeeds on a simple domain (smoke or existing tests)  
- [ ] Pass dependency ids resolve (no missing-dependency runtime failures)  

## Output

- `DomainModelAnalyzer.cs` + pass dependency arrays  
- Status Done  

## Out of Scope

- DslCompiler changes  
- New diagnostics  
- StoragePass move  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
