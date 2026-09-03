# P1 temporal — research / spike track (not CURRENT)

**Date:** 2026-08-06  
**Status:** **Parked research** — concepting only  
**Product admit order after pipeline:** **P3 → P2** (this file is **not** in that queue)  
**Parent vision:** [`domain-dsl-absorption-proposals.md`](domain-dsl-absorption-proposals.md) § P1 · experiment [`docs/plans/archive/experiments/DOMAIN-DSL-SPEC.md`](archive/experiments/DOMAIN-DSL-SPEC.md)

---

## Purpose

Capture how to model dates / `Now` / duration arithmetic **before** solidifying a `p1-*` implementation suite. Do not start production temporal work until this research produces design locks and an explicit master-roadmap admit.

## Open questions — answered 2026-08-06 (spike ran)

1. **Core seed vs built-in temporal pack + specialization registry (absorption §2.5–2.6)?**
   → **Built-in temporal pack.** Core keeps the generic seams only: `DateOperation` as the *resolved* IR shape, open grammar forms (`N unitIdent`, `Now`, `±`) with fail-closed "unknown specialization", and generic lowering of resolved IR → Syntax. Pack registers units (`days`/`months`/optional `weeks`), binary specializations (`date ± duration` → `DateOperation` kind), and the clock leaf. Authoring registration channel is **grammar-integration GI-4** (pack grammar registration) — see `grammar-integration.md`.
2. **What is product-minimal authoring: `Now - 12 days`, compare only, assign RHS?**
   → `Now - 12 days` / `DueDate + 14 days` as **assign RHS**; **compare** (`ExpiryDate < Now`) in policies. No `schedule at` (host P9), no new date-default forms beyond existing `default now`/`today` strings.
3. **Policy preprocess vs full lower for date ops?**
   → Same split as store-aware `Rel exists`: store/policy path **preprocesses** (resolve `Now` once per evaluation via injectable clock; `DateOperation` stays stored IR); export path **lowers fully** to CLR members (`UtcNow`, `AddDays`, `Subtract`). No new dual path.
4. **Host clock surface for tests (injectable) vs wall clock?**
   → **CLR host: `System.TimeProvider`** (net10.0; default `TimeProvider.System`; tests inject `FakeTimeProvider` or a fixed subclass). No custom clock interface. Domain IR stays platform-agnostic — `Now`/`today` are expression nodes; `TimeProvider` is the CLR host adapter (built-in-type mapping rule: CLR mapping is one adapter). Timers come free for P9 scheduling.
5. **What stays pack-only (business days, TZ)?**
   → Business days, fiscal calendars, time zones, alternate clocks — optional packs on the same seams, not core forks.

## Spike outputs (research ran 2026-08-06)

- [x] Short design lock doc (or ADR stub) with chosen style A/B/C/D — **done: [`p1-temporal-design-lock.md`](p1-temporal-design-lock.md)** (A/B/C/D vocabulary is stale — absorption now uses "built-in pack" decision, adopted there)
- [x] Inventory of existing `DateOperation` / `now` / builtins in tree — **done: design lock §inventory** (`DateOperation` + `DateOp` factory, `now`/`utcnow`/`today`/`guid` as `DefaultValueConstraint` strings only, no DSL tokens, lowering + tests enumerated)
- [x] One spike sketch (optional file-based app or failing test list) — **not** product merge — **done: design lock §appendix (doc-only failing-test sketch)**
- [x] Decision: solidify `p1-*` suite or defer — **defer** (design lock §decision; admit order P3 → P2 → grammar-integration → explicit P1 admit)

## Do not

- Parallel P1 with P3/P2 CURRENT  
- ~~Grammar re-base as prereq~~ — **superseded 2026-08-06 (user direction):** [`grammar-integration.md`](archive/completed-2026-08-mid/grammar-integration.md) (GI-1..GI-8) is a committed plan that lands **before** this work; P1 pack authoring rides on GI-4 registration, so this is no longer a speculative re-base
- Ship incomplete `schedule at` as domain truth without host adapter  

## Related product next

| Order | Suite |
|-------|--------|
| 1 | [`simple-agent-tasks/p3-README.md`](simple-agent-tasks/p3-README.md) — return types |
| 2 | [`simple-agent-tasks/p2-README.md`](simple-agent-tasks/p2-README.md) — multi-hop |
| 3 | [`grammar-integration.md`](archive/completed-2026-08-mid/grammar-integration.md) — GI-1..GI-8 (user direction: before P1) |
| later | P1 after this research + explicit admit |
