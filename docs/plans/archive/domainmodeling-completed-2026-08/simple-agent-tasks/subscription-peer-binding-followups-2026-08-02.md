# Subscription peer binding residuals — 2026-08-02 (r2)

**Source review:** [`../../agent/reviews/2026-08-02-subscription-peer-binding-r2.md`](../../agent/reviews/2026-08-02-subscription-peer-binding-r2.md)  
**Prior closed full-send:** [`subscription-peer-binding-followups-2026-08-01.md`](./subscription-peer-binding-followups-2026-08-01.md)  
**Status:** `[x]` Closed 2026-08-02  

## Tasks

- [x] **F11** — Entity-level `when` runs full `ValidateSubscription` (binding + stages + rel); peer binder still error; dispatch still warn  
- [x] **F12** — `Export_PeerDependentSubscription_Throws`  
- [x] **F13** — Nested peer + peer assign-target analysis tests  
- [x] **F14** — Any quantifier + PeerBinding runtime peer copy  
- [x] **F15** — Link/Unlink `Target` collected and peer-rewritten  
- [x] **F16** — Runtime reject peer assign target; unbound-root message when binder present  

## Done definition

1. [x] F11 closed with analysis + guide agreement.  
2. [x] F12–F14 green oracles.  
3. [x] No false open residuals for r2 issues.  
4. [x] Suite green (**1785**).
