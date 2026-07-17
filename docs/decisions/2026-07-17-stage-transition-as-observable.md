# Stage Transition as the Authorable Observable

**Date:** 2026-07-17  
**Status:** Accepted  
**Deciders:** Architecture team  
**References:** [`dsl-sync-toward-phase1.md`](../plans/v2-to-v3/dsl-sync-toward-phase1.md), [`domain-modeling-dsl-tour-feedback.md`](../experiments/domain-modeling-dsl-tour-feedback.md)

---

## Context

The DSL v0.3 direction settled on `when property Stage { effects }` as the mechanism for reacting to lifecycle changes. Stage transitions — not events — are the authorable observable. The engine IR, however, still had first-class `Event`, `EventSubscription`, `EventCorrelationBinding`, `EventSubscriptionRoutingMode`, and `PublishEventEffect` types — a separate event-centric vocabulary that the DSL had already abandoned.

Keeping both vocabularies forces analyzers to maintain two parallel worldviews and widens the gap between what authors write and what the engine understands.

## Decision

**Stage transitions are the only first-class authorable observable in the domain IR.** The following types are removed from the product authoring surface:

| Removed | Replacement |
|---------|-------------|
| `Event` (domain type) | Engine-internal runtime observation only (not a `DomainType`) |
| `PublishEventEffect` | Stage entry IS the "publish" |
| `EventSubscription` + routing modes | `StageSubscription` on `Stage` |
| `EventCorrelationBinding` | Correlation is implicit via relationship path |
| `Entity.Events` / `Entity.EventSubscriptions` | `Stage.Subscriptions` |

## Consequences

**Positive:**
- Single authorable lifecycle vocabulary (stages → transitions → subscriptions)
- Fewer IR types to reason about (5 event types deleted, 1 `StageSubscription` type added)
- Analyzer surface shrinks: 5 event-centric analyzer passes reduced to 3 stage-subscription passes
- Relationship path is the only correlation mechanism — no separate key-matching surface
- `StageSubscription` effects are ordinary `Effect` records — same lowering path as actions

**Negative:**
- Existing domains that used `PublishEventEffect` with fine-grained event payloads must be rewritten to convey data through entity property access on the transitioning instance
- Runtime observation ("entity X entered stage Y at time Z") is distinct from the authoring surface — the lowerer produces this as runtime data, not a `DomainType`

## Compliance

- `PublishEventEffect` removed from all product paths
- `EventSubscription`/`EventCorrelationBinding`/`EventSubscriptionRoutingMode` deleted
- `StageSubscription` record + quantifier enum on `Stage`
- Analyzers register under `DMSS*` code range; old `DMEV*` codes retired
- `docs/CORE.md` domain section updated to reflect stage-observation model
