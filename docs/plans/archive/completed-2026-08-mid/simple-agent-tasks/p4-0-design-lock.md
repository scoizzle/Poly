# P4-0 — Design lock (read + confirm)

**Difficulty:** S  
**Status:** `[x]` — PASSED 2026-08-06

## Design locks (confirmed)

1. **Grammar:** `when [any|all] Rel Stage[, Stage…] [as name] { effects }` — matches
   § P4 of `domain-dsl-absorption-proposals.md` and the p4-README grammar lock.
2. **Default Each:** Omitted quantifier = `Each` (current product behavior unchanged).
3. **Zero new runtime:** `DomainInstanceStore.NotifyTransition` already dispatches
   `Each` (per-transition) and `Any`/`All` (set-state check on linked targets) —
   parse/print only.
4. **Set-state-after-transition semantics:** `Any` fires once when ≥1 linked target is
   in a matching stage; `All` fires once when every linked target is in a matching stage.
   Both evaluate the current linked-target set after the transition, not "every peer
   bag at once". Document in guide (p4-4).
5. **Peer binder** `as name` remains valid with Any/All (peer = transitioned instance).
6. **Parse pattern to copy:** `invoke any|all Rel.Action` — identifier text match
   (`any`/`all`, OrdinalIgnoreCase) before the relationship name; no new token kind.
7. **Analysis already warns** singular (OneToOne/ManyToOne) + Any/All
   (`SubscriptionAnalyzer` isSingularFromSource, `SubscriptionContractMismatch`);
   undefined quantifier check exists. P4-2 adds DSL-level fail-closed tests.
8. **Non-goals:** no dates / multi-hop / actors; no new runtime dispatch algorithm;
   store untouched unless a real bug is found (then file finding).

## Objective

Confirm P4 grammar and non-goals in one short note (task file progress notes or parent § P4 unchanged). No code.

## Required reading

- Absorption proposals § P4  
- `StageSubscription` / quantifier enum in DomainModeling  
- Invoke `any`/`all` parse pattern (copy style)  

## Exact steps

1. Confirm grammar: `when [any|all] Rel Stage[,…] [as name] { }`.  
2. Confirm default Each; no runtime change.  
3. Note All/Any fire on set state after transition (document in notes).  
4. Mark this task Done only after reading — implement starts at P4-1.

## Verification

- [x] Notes record locks
- [x] No production edits

## File ownership

- Notes only

## Status

**Status:** DONE — locks recorded; implementation starts at P4-1.  
