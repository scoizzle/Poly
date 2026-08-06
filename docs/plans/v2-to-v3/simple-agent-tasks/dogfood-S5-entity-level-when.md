# Dogfood S5 — Entity-level `when` (always-active subscriptions)

**Queue:** [`dogfood-README.md`](dogfood-README.md)  
**Protocol:** [`../mcp-dogfood-protocol.md`](../mcp-dogfood-protocol.md)  
**Wave:** 2 (shipped SPE entity-level when)  
**Status:** `[ ]` Not Started  
**Difficulty:** Medium  
**Prereq:** Prefer S4 attempted; can run standalone  
**Est. session time:** 40–60 min  

---

## Goal

Author **entity-level** stage subscriptions (not only nested under a single stage block) and prove they fire on peer transitions **regardless of which stage the subscriber is currently in** (within product rules).

## Concept under test

**Entity-level `when` dispatch:** `RuntimeContractAnalyzer` plans on entity; `NotifyTransition` runs stage plan then entity plan; empty plan when none.

---

## Domain sketch (minimum)

- Two entities + relationship (can reuse S4 topology).  
- Place `when Rel Stage { … }` (or `as name`) on the **entity** surface per guide — not only inside one stage.  
- Subscriber has **at least two stages** (e.g. Draft / Active).  
- Prove: with subscriber in stage A, peer transition still runs entity-level when; optionally move subscriber to stage B and prove still fires.

If product guide scopes entity-level when differently, document actual guide contract and test **that**.

---

## Success checklist

| # | Criterion | Met? |
|---|-----------|------|
| 1 | Domain authors with **entity-level** `when` (guide-legal) | |
| 2 | Analysis clean | |
| 3 | Linked instances; peer transition via `invoke_action` | |
| 4 | Subscription fires when subscriber is in stage A | |
| 5 | Subscription still fires (or guide-documented non-fire) when subscriber is in stage B | |
| 6 | Report contrasts entity-level vs stage-scoped `when` behavior | |

**PASS** = entity-level when is authorable and behaves as guide claims across subscriber stages.

**FAIL useful** = cannot place entity-level when; silent no-op; stage-scoped only; guide lies.

---

## Forbidden workarounds

- Only testing stage-scoped `when` and claiming entity-level  
- Platform code fixes mid-discovery  
- Collapsing to a single-stage entity to dodge the concept  

---

## Session steps (suggested)

1. Guide search: entity-level vs stage `when`  
2. Author domain with entity-level subscription  
3. Runtime path with subscriber in Draft (or first stage)  
4. Transition subscriber to second stage; repeat peer transition  
5. Report  

---

## Report output

`docs/plans/v2-to-v3/agent-summaries/dogfood/DOGFOOD-S5-YYYYMMDD.md`

---

## Status tracking

**Claimed by:**  
**Started:**  
**Report path:**  
**Result:** PASS / FAIL / PARTIAL  
