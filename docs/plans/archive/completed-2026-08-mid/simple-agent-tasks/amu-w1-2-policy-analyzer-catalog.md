# AMU-W1.2 — PolicyConstraintAnalyzer catalog-only name resolve

**Wave:** 1  
**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** W0 preferred  
**Parallel OK with:** W1.1, W1.3  

## Objective

Policy expression analysis resolves relationships / owned targets via catalog or Semantic lookup metadata, not ad-hoc `domain.Relationships.FirstOrDefault` on domain-bound paths.

## Required reading

- `Poly/DomainModeling/Analysis/PolicyConstraintAnalyzer.cs`  
- `DomainSemanticLookupExtensions`  
- Policy expression analysis tests  

## Exact steps

1. Find all relationship/entity scans in PolicyConstraintAnalyzer.  
2. Route through existing lookup helpers when DTLM/RLM/catalog available.  
3. Preserve fail-closed / diagnostic codes; do not change empty-set policy runtime semantics.  
4. Tests: unknown rel, reverse nav if covered.

## Verification

- [ ] Build + policy analyzer tests green  
- [ ] No new metadata bag without consumer  

## File ownership

- **Edit:** `PolicyConstraintAnalyzer.cs` + its tests  
- **Do not edit:** EffectAnalyzer, runtime store, MCP  

## Status

**Status:** Not Started  
