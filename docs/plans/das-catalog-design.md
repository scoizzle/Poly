# Domain catalog design (DAS W1.1)

**Status:** Implemented as dual-write (catalog + existing bags)  
**Date:** 2026-07-31  

## Shape

`DomainCatalogMetadata` (domain-keyed):

| Field | Source |
|-------|--------|
| `Types` | `DomainTypeLookupMetadata` |
| `Relationships` | `RelationshipLookupMetadata` |
| `Index` | `MutationTargetIndexMetadata` |
| `ActionsByEntityName` | Per-entity `ActionResolutionMetadata` |

## Ownership

- **Publisher:** `DomainCatalogPass` after Semantic + RuntimeContract.
- **SA fallthrough:** only `TryResolveAction` (catalog ARM → entity ARM).
- **Effective policies:** StageCapability first, then Index entity+stage maps.
- **Subscription plans:** remain stage-keyed (`SubscriptionDispatchPlanMetadata`).

## Migration

1. Dual-write: keep DTLM/RLM/MTI/ARM publishers; catalog composes them.
2. Consumers prefer catalog (lookups).
3. Later wave may delete redundant public consumption of raw MTI/ARM (not required for W1 green).
