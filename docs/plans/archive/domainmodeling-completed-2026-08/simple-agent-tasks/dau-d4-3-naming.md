# Micro-Task: DAU.D4.3 — Naming hygiene (optional)

**Suite:** [`dau-README.md`](dau-README.md) **#D4.3**  
**Parent:** [`../domain-analysis-unification.md`](../domain-analysis-unification.md)  
**Difficulty:** Small  
**Prereq:** **D3.7**  
**Status:** `[ ]` Optional — skip if timeboxed  

## Objective

Rename remaining domain-fact `*Pass` types to `*Analyzer` for consistency **only if** low risk (mechanical rename). Skip StoragePass/TransportPass if generators/docs use Ids heavily and churn is high — document Ids stay stable.

## Exact Steps

1. List `*Pass.cs` under Analysis.  
2. Rename **only** types where PassName/Id can stay the same string for dependency stability, **or** update all Dependencies arrays in the same PR.  
3. Prefer one rename PR for EffectTopologyPass / OwnershipAggregatePass / BehaviorPass / CrossReferencePass / EntitySyntaxPass if safe.  
4. Full suite green.

## Definition of Done

- [ ] Either renames done with suite green, **or** task marked skipped with note “deferred naming”  
- [ ] No behavior change  

## Out of Scope

- Module splits  
- Public API renames outside DomainModeling.Analysis  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
