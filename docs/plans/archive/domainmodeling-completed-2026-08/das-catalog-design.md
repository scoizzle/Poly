# Domain catalog design (DAS W1.1 / W1.4)

**Status:** Dual-write of ARM/MTI **retired** (W1.4). Catalog is sole product name→member map.  
**Date:** 2026-07-31  

## Shape

`DomainCatalogMetadata` (domain-keyed):

| Field | Source |
|-------|--------|
| `Types` | Intermediate `DomainTypeLookupMetadata` (Semantic) |
| `Relationships` | Intermediate `RelationshipLookupMetadata` (Semantic) |
| `Index` | Built **only** in `DomainCatalogPass` (`MutationTargetIndexMetadata` shape) |
| `ActionsByEntityName` | Built **only** in `DomainCatalogPass` (per-entity `ActionResolutionMetadata` shape) |

## Ownership

- **Publisher:** `DomainCatalogPass` after Semantic + RuntimeContract. Sole write site for action maps and mutation-target index. Embeds DTLM/RLM; does not re-publish entity-keyed ARM or domain-keyed MTI.
- **SA fallthrough:** **only** `DomainSemanticLookupExtensions.TryResolveAction` (catalog ARM). Empty stage-copy (no effects/policies) → entity action; parameters ignored (`AddActionToStageChange` copies them).
- **Effective policies:** StageCapability first, then catalog `Index` entity+stage maps.
- **Subscription plans:** stage-keyed (`SubscriptionDispatchPlanMetadata`); built in RuntimeContract from relationship contracts + intermediate DTLM (notify identity). Not a second action/policy graph.

## Consumer API

All product consumers go through `DomainSemanticLookupExtensions` (or its helpers `GetCatalog` / `GetActionResolution` / `GetMutationIndex` / `GetTypeLookup` / `GetRelationshipLookup`). Domain-keyed helpers are **catalog-only**. Domain-less type/relationship helpers may read intermediate Semantic DTLM/RLM.

| Consumer | Path |
|----------|------|
| Runtime `InvokeActionInternal` | `TryResolveAction(domain, …)` |
| Runtime entity/rel resolve | `TryGetEntity(domain, …)` / `TryGetRelationship(domain, …)` |
| MCP describe action/policy/rel | `GetActionResolution` / `GetMutationIndex` / `TryGetRelationship(domain, …)` |
| Evolution mutation index | `GetMutationIndex` |
| Lowering type/rel | `GetTypeLookup` / `GetRelationshipLookup` |

## Remaining bags (ownership matrix)

| Bag | Publisher | Key | Role after W1.4 |
|-----|-----------|-----|-----------------|
| **DomainCatalogMetadata** | DomainCatalogPass | Domain | **Authoritative** name→member product catalog |
| DomainTypeLookupMetadata | SemanticDomainAnalyzer | `default` | Intermediate mid-pipeline type index; embedded in catalog |
| RelationshipLookupMetadata | SemanticDomainAnalyzer | `default` | Intermediate mid-pipeline rel index; embedded in catalog |
| RelationshipContractMetadata | RuntimeContractAnalyzer | `default` | Runtime relationship contracts (not full action/policy map) |
| SubscriptionDispatchPlanMetadata | RuntimeContractAnalyzer | Stage | Stage-keyed notify dispatch plan |
| EntityStructureMetadata | EntityStructureAnalyzer | Entity | Structure derive (key, stages handle, …) |
| StageCapabilityMetadata | CapabilityAnalyzer | Stage | Effective surface (W2) |
| ~~entity-keyed ActionResolutionMetadata~~ | — | — | **Retired** as dual-write; type lives inside catalog only |
| ~~domain-keyed MutationTargetIndexMetadata~~ | — | — | **Retired** as dual-write; type lives inside catalog only |

## Migration history

1. Dual-write: keep DTLM/RLM/MTI/ARM publishers; catalog composes them. **(W1.2)**
2. Consumers prefer catalog (lookups). **(W1.3)**
3. End dual-write: catalog builds/publishes ARM+MTI only; product dual-read removed. **(W1.4)**
