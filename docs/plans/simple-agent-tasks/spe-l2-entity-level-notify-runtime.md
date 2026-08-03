# SPE-L2 — Entity-level when in NotifyTransition

**Stream:** L  
**Difficulty:** M  
**Status:** `[x]`  
**Soft prereq:** L1  

## Objective

`DomainInstanceStore.NotifyTransition` fires entity-level subscriptions for linked subscribers **regardless of subscriber current stage**.

## Required reading

- L1 metadata  
- `DomainInstanceStore.NotifyTransition`  
- Stage dispatch path (sibling — keep behavior)  

## Exact steps

1. After or alongside stage dispatch, load entity-level plan for each subscriber.  
2. For matching relationship + target stage + link + quantifier rules, call `ExecuteSubscriptionEffects` with `PeerBinding`.  
3. **Order (lock):** stage-scoped handlers first, then entity-level (document in remarks).  
4. Depth/cascade: same max depth rules as stage path.  
5. Runtime test: subscriber in stage **without** stage-local `when`, entity-level `when Rel Stage` → effects run.  
6. Sibling: stage-local still works when subscriber is in the right stage.

## Verification

- [x] Entity-level fires off-stage  
- [x] Stage-local still fires  
- [x] PeerBinding on entity-level works if analysis allows (L3 may enable analysis) — runtime passes `PeerBinding` from plan; analysis still errors on entity-level peer (L3)  
- [x] Build + suite green for DomainModeling store tests  

## File ownership

- **Edit:** `DomainInstanceStore.cs`, runtime tests  
- **Do not edit:** exporter peer lowering  

## Progress notes

### 2026-08-02 — implement + verify (pass, severity suggestion)

**Implement success:** true · **Verify pass:** true · **Severity:** suggestion  
Source review of `DomainInstanceStore.NotifyTransition` (build/suite not re-executed; no shell for git status/diff). Judged post-change source + listed tests against SPE-L2 AC.

- **Runtime:** After catalog/RCM, per non-deleted subscriber runs stage plan (if `CurrentStage` resolves) then always loads `SubscriptionDispatchPlanMetadata` on `Entity` (throw if missing) and shared `DispatchMatchingEntries` (link, target stage, quantifiers Each/Any/All, `PeerBinding`, cascade depth max 10).
- **Order lock:** Remarks lock stage-then-entity.
- **Tests:** `EntityLevelSubscription_Fires_WhenSubscriberNotInStageWithWhen`; `StageLocalSubscription_StillFires_AlongsideEntityPath`; `EntityLevelAndStageSubscription_StageFirstThenEntityLevel` (weak order oracle); `NotifyTransition_Throws_WhenEntityLevelSubscriptionPlanMissing`.
- **Peer / analysis:** Peer analysis still errors on entity-level peer (L3). Runtime passes `PeerBinding` from plan when present.
- **Fail-closed:** `RuntimeContractAnalyzer` publishes empty entity bags so missing-plan throw is coherent. No second notify consumer of plans (stage/entity dual path only in store).

## Status

**Status:** Done (SPE-L2) — entity-level dispatch in `NotifyTransition`; stage path first, then entity bag; L3 owns peer analysis + guide.

