# P4-3 — Runtime goldens (Any/All + Each regression)

**Difficulty:** M  
**Status:** `[x]` — DONE 2026-08-06

## Implementation notes

- New `Poly.Tests/DomainModeling/P4SubscriptionQuantifierDslTests.cs` — DSL-authored
  subscriptions driven through `DomainInstanceStore` + `DomainEntityInstance`:
  - `WhenAny_FiresOnceWhenAnyLinkedLoanIsOverdue` — `when any loans Overdue`: fires
    once per transition once ≥1 linked loan is Overdue (set-state after transition).
  - `WhenAll_FiresOnlyWhenEveryLinkedLoanIsOverdue` — `when all loans Overdue`: does
    **not** fire while only 1 of 2 loans matches; fires exactly once when both match.
  - `WhenNoKeyword_Each_FiresPerTransitionWithPeer` — default Each regression with
    `as loan` binder: fires per matching transition, peer = transitioned instance.
- Zero production/runtime edits — confirms `DomainInstanceStore.NotifyTransition`
  already implements Any/All for DSL-authored plans (p4 hard rule: no new runtime).
- Verified: 1855/1855 green (3 new tests).
Prove store notify still implements Any/All when subscriptions are authored via DSL (not only IR fixtures). Each regression remains green.

## Required reading

- DomainInstanceStore notify / subscription dispatch  
- Existing Any/All IR tests if present  

## Exact steps

1. Author domain via parse or evolution with `when any` (and `when all` if cheap).  
2. Link peers; transition; assert fire-once / set semantics.  
3. Regression: `when Rel Stage` (Each) still per-element.  
4. Prefer DomainEntityInstance / subscription product tests.

## Verification

- [ ] Any golden green  
- [ ] Each regression green  
- [ ] All golden optional if time — note if deferred  

## File ownership

- **Edit:** tests primarily; store only if bug found (file fix finding if large)  
- **Do not edit:** guide  

## Status

**Status:** Not Started  
