# Subscription peer binding follow-ups — 2026-08-01

**Source review:** [`../../agent/reviews/2026-08-01-subscription-peer-binding.md`](../../agent/reviews/2026-08-01-subscription-peer-binding.md)  
**Status:** `[x]` Closed 2026-08-01 (full-send) — **residuals:** [`subscription-peer-binding-followups-2026-08-02.md`](./subscription-peer-binding-followups-2026-08-02.md) (r2)

## Tasks

- [x] **F1** — Fail-closed unbound path-prefix without `as name` + guide + test  
- [x] **F2** — C# export refuses peer-dependent subscriptions (`InvalidOperationException`)  
- [x] **F3** — Nested peer path-prefix rejected at analysis; guide narrowed to scalar  
- [x] **F4** — Peer binder assign target rejected  
- [x] **F5** — `BindPeerInExpression` covers quantifiers + `DateOperation`  
- [x] **F6** — Entity-level: warn for store-notify honesty; **error** if `as name`  
- [x] **F7** — Analysis collect includes create-in + invoke filter  
- [x] **F8** — Oracles: F1, event, PeerBinding on plan, entity-level, cleanup flag  
- [x] **F9** — Removed dead `_eventValues` / `SetPeerInstance`  
- [x] **F10** — Diagnostic comment + remove `SubscriptionKey` includes PeerBinding  

## Done definition

1. [x] F1 green (docs + analysis + test agree).  
2. [x] F2 dispositioned (export refuses peer-dependent).  
3. [x] F3–F8 closed with honest guide text.  
4. [x] Suite green (**1780**); no product `event.*` path.
