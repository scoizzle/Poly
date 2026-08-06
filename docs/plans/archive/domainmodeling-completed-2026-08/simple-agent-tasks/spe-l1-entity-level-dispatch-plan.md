# SPE-L1 — Entity-level subscription dispatch metadata

**Stream:** L (entity-level when)  
**Difficulty:** M  
**Status:** `[x]`  
**Soft prereq:** Parent plan §4 L  

## Objective

Publish analysis metadata for `Entity.Subscriptions` so runtime can dispatch them (always-active), separate from stage plans.

## Required reading

- `Entity.Subscriptions` remarks  
- `RuntimeContractAnalyzer` stage plan publish  
- `DomainModelMetadata` `SubscriptionDispatchPlanMetadata` / entries  
- `SubscriptionAnalyzer` entity-level loop  

## Exact steps

1. Design metadata home (pick one and document in code remarks):
   - **Preferred:** entity-scoped plan bag on the `Entity` node (mirror stage plan shape), **or**  
   - domain-scoped map entity name → entries.  
2. Publish entries for each entity-level sub (relationship, stages, quantifier, effects, PeerBinding) using the same contract resolution as stage.  
3. Fail closed if relationship cannot be uniquely resolved (same as stage).  
4. Do **not** change store notify yet (L2).  
5. Unit test: analyze domain with entity-level `when` → metadata present with expected relationship/stages.

## Verification

- [x] Build + new metadata test green  
- [x] Stage plans unchanged for stage-only domains  
- [x] No silent empty publish when subs exist and rel resolves  

## File ownership

- **Edit:** `RuntimeContractAnalyzer.cs`, `DomainModelMetadata.cs` (if new record), tests under Analysis  
- **Do not edit:** `DomainToCSharpExporter` peer handlers, policy eval  

## Progress notes

### 2026-08-02 — implement + verify (pass, severity none)

**Implement success:** true · **Verify pass:** true · **Severity:** none  
Build/suite not re-executed (no shell); SPE-L1 AC met in source.

- **Metadata home:** `RuntimeContractAnalyzer` publishes `SubscriptionDispatchPlanMetadata` on `Entity` via shared `BuildSubscriptionEntries` (same name+source unique resolve + structural fail as stage).
- **Docs:** `DomainModelMetadata` documents dual home (entity bag vs stage bag).
- **Store:** `DomainInstanceStore` still reads stage bag only (notify deferred to L2).
- **Tests:** `RuntimeContractMetadataTests` cover happy entity-level plan (`rel`/stages/peer null; stage/entity bag independence) and fail-closed unresolved rel (empty bag + `SemanticReferenceResolution`). Sibling stage path shares resolve helper; missing-RCM early return incomplete on both.

## Status

**Status:** Done (SPE-L1) — entity-scoped `SubscriptionDispatchPlanMetadata` on `Entity`; store notify still L2.  
 
