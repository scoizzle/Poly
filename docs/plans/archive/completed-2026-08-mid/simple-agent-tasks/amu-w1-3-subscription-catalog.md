# AMU-W1.3 — SubscriptionAnalyzer / RuntimeContract catalog lookups

**Wave:** 1  
**Difficulty:** M  
**Status:** `[x]` — DONE 2026-08-06
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

- [x] Build + subscription/runtime contract tests green (1842/1842 full suite)
- [x] Peer binder / entity when goldens still pass (SurfaceExtensionDogfoodTests green)

## Implementation notes

`SubscriptionAnalyzer.cs` — 2 relationship-name resolves now via
`ResolveRelationshipLookup` (catalog preferred, intermediate RLM fallback):
1. `ValidateSubscription` — `domain.Relationships.FirstOrDefault` → RLM `TryGetValue` + source match
2. Causality cycle edge graph (per-sub loop was rebuilding a scan per subscription) → hoisted single
   lookup, per-sub `TryGetValue`

`RuntimeContractAnalyzer` had no linear `domain.Relationships` scans (checked) — it consumes
`RelationshipContractMetadata`/`SubscriptionDispatchPlanMetadata` bags only; no edit needed.
Intentional full enumerations (O(n) once over typed contract list) left as-is per task note.

- **Edit:** `SubscriptionAnalyzer.cs`, `RuntimeContractAnalyzer.cs` as needed + tests  
- **Do not edit:** EffectAnalyzer, PolicyConstraintAnalyzer, MCP tools  

## Status

**Status:** Done — 2026-08-06 (see Implementation notes)  
