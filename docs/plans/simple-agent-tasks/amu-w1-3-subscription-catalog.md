# AMU-W1.3 — SubscriptionAnalyzer / RuntimeContract catalog lookups

**Wave:** 1  
**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** W0 preferred  
**Parallel OK with:** W1.1, W1.2  

## Objective

Subscription and runtime-contract analysis that resolve relationship names for plans/contracts use Semantic/catalog lookups when available, reducing full `domain.Relationships` rebuilds where a lookup already exists.

## Required reading

- `SubscriptionAnalyzer.cs`, `RuntimeContractAnalyzer.cs`  
- `RelationshipContractMetadata`, `SubscriptionDispatchPlanMetadata`  
- Notify / subscription tests  

## Exact steps

1. Identify name resolve vs intentional full enumeration (contracts over all rels may still walk — OK if O(n) once and uses typed list from lookup).  
2. Prefer `RelationshipLookupMetadata` / catalog for name→rel resolution.  
3. Do not break entity-level / stage plan publication or peer binder fail-closed.  
4. Regression tests for subscription plans still green.

## Verification

- [ ] Build + subscription/runtime contract tests green  
- [ ] Peer binder / entity when goldens still pass  

## File ownership

- **Edit:** `SubscriptionAnalyzer.cs`, `RuntimeContractAnalyzer.cs` as needed + tests  
- **Do not edit:** EffectAnalyzer, PolicyConstraintAnalyzer, MCP tools  

## Status

**Status:** Not Started  
