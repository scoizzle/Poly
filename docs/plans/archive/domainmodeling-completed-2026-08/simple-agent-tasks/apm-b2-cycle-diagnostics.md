# Micro-Task: APM.B2 — Cycle diagnostics (one story)

**Suite:** [`apm-README.md`](apm-README.md) **#B2**  
**Parent:** [`../analysis-pipeline-merge.md`](../analysis-pipeline-merge.md) Phase B  
**Difficulty:** Medium  
**Estimated Context:** ~12k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** Phase A Gate  

## Objective

Ship **one** cycle diagnostic path — prefer wiring existing **`CrossReferencePass`** (already has DFS + `CrossReferencePass.CycleDetected`) into the domain pipeline rather than inventing DMEFF010 on topology unless CrossReference is wrong for the case.

## Required Reading

- `Poly/DomainModeling/Analysis/CrossReferencePass.cs`  
- Parent Phase B DMEFF010 note  
- Domain pipeline registration  

## Exact Steps

1. Evaluate: does CrossReferencePass cover create-in / invoke / subscription cycles needed?  
2. **Preferred:** register `CrossReferencePass` after topology + aggregate; stabilize diagnostic code (rename to DM* if required for consistency).  
3. **Alternative:** implement topology cycle warning only if CrossReference is insufficient — document why.  
4. Test with a crafted cyclic fixture.  
5. Do not emit both CrossReference and a second duplicate warning for the same cycle.  

## Verification

- [ ] One clear cycle warning on fixture  
- [ ] Suite green  
- [ ] Decision recorded in parent plan decision/notes  

## Out of Scope

- Full dependency graph UI  
- Errors that block all evolution on cycles (start Warning)  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
