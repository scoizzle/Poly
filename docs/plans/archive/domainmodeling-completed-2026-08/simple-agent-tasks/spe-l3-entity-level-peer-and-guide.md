# SPE-L3 — Entity-level peer binder + analysis/guide honesty

**Stream:** L  
**Difficulty:** S  
**Status:** `[x]`  
**Soft prereq:** L2  

## Objective

Align analysis and guide with runtime: entity-level `when` is dispatched; `as name` allowed under the same rules as stage.

## Required reading

- L2 runtime  
- `SubscriptionAnalyzer` entity-level peer error / warn  
- Guide §7 entity-level bullets  

## Exact steps

1. Remove hard error “entity-level peer binder not supported” once L2 proves dispatch (keep validation of bindings).  
2. Soften or remove “not dispatched by VM store” **warning** for entity-level (or change to hint about stage vs entity-level choice).  
3. Guide §7: entity-level = always-active; stage-level = only while in that stage; both support optional `as name`.  
4. Tests: entity-level + peer binder copies peer field; analysis no longer errors solely for entity-level peer.

## Verification

- [x] Analysis accepts entity-level `as name` when rel/stages valid  
- [x] Runtime golden peer + entity-level  
- [x] Guide matches  
- [x] Full suite green (L-relevant + guide; 1 pre-existing unrelated `SimulatePolicy_RelationshipJson_Accepted` fail)

## File ownership

- **Edit:** `SubscriptionAnalyzer.cs`, guide §7 entity-level bullets, tests  
- **Do not rewrite** export peer sections (E owns)  

## Status

**Status:** Done  

### 2026-08-02 — implement

- Dropped entity-level peer hard error and “not dispatched” warn in `SubscriptionAnalyzer`; entity-level uses same `ValidateSubscription` as stage.
- Guide §7: placement table (stage vs entity always-active), both support `as name`; entity-level peer example.
- Tests: `EntityLevelSubscription_AnalysisAccepts_WhenRelAndStagesValid`, `EntityLevelSubscription_WithPeerBinding_AnalysisAccepts`, `EntityLevelSubscription_PeerBinding_CopiesPeerProperty`; unbound-path fail-closed retained.

### 2026-08-02 — verify

- **Pass** (severity: suggestion). Suite not re-run in verifier (no shell).
- `SubscriptionAnalyzer`: `entity.Subscriptions` → `ValidateSubscription` (shared peer binding rules); no production messages for entity-level peer hard error or not-dispatched warn.
- `RuntimeContractAnalyzer.PublishEntitySubscriptionDispatchPlan` copies `PeerBinding`; `DomainInstanceStore.DispatchMatchingEntries` passes `entry.PeerBinding`.
- Guide §7 placement table (always-active + `as name`) and entity-level peer example match.
- Tests present: `EntityLevelSubscription_AnalysisAccepts_WhenRelAndStagesValid`, `WithPeerBinding_AnalysisAccepts`, `PeerBinding_CopiesPeerProperty`, `UnboundPathPrefix_AnalysisError`.

