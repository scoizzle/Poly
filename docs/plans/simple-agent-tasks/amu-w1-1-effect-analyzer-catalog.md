# AMU-W1.1 — EffectAnalyzer catalog-only name resolve

**Wave:** 1  
**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** W0 preferred  

## Objective

When domain analysis context has type/relationship lookups (DTLM/RLM/catalog), `EffectAnalyzer` resolves relationship and entity names via catalog/helpers instead of linear `domain.Relationships` / `Types.OfType` scans on domain-bound paths.

## Required reading

- `Poly/DomainModeling/Analysis/EffectAnalyzer.cs`  
- `Poly/DomainModeling/Analysis/DomainSemanticLookupExtensions.cs`  
- Existing effect-binding tests  

## Exact steps

1. Inventory all rel/entity name lookups in EffectAnalyzer.  
2. Prefer `TryGetRelationship` / `TryGetEntity` / catalog helpers when domain + lookup metadata available.  
3. Keep standalone (`Domain == null`) reduced contract if already documented — do not invent dual soft success.  
4. Fail closed when analysis present and required lookup bag missing (match DACR style).  
5. Add/adjust tests for unknown rel / wrong source with analysis present.

## Verification

- [ ] Build + relevant EffectAnalyzer tests green  
- [ ] Domain-bound happy/error paths use helpers (code review)  
- [ ] No new parallel index  

## File ownership

- **Edit:** `EffectAnalyzer.cs`, related tests under `Poly.Tests/DomainModeling/`  
- **Do not edit:** PolicyConstraintAnalyzer, SubscriptionAnalyzer, exporter, MCP  

## Status

**Status:** Not Started  
