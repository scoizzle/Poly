# P1 temporal — research / spike track (not CURRENT)

**Date:** 2026-08-06  
**Status:** **Parked research** — concepting only  
**Product admit order after pipeline:** **P3 → P2** (this file is **not** in that queue)  
**Parent vision:** [`domain-dsl-absorption-proposals.md`](domain-dsl-absorption-proposals.md) § P1 · experiment [`docs/experiments/DOMAIN-DSL-SPEC.md`](../experiments/DOMAIN-DSL-SPEC.md)

---

## Purpose

Capture how to model dates / `Now` / duration arithmetic **before** solidifying a `p1-*` implementation suite. Do not start production temporal work until this research produces design locks and an explicit master-roadmap admit.

## Open questions (fill during spike)

1. Core seed vs built-in temporal pack + specialization registry (absorption §2.5–2.6)?  
2. What is product-minimal authoring: `Now - 12 days`, compare only, assign RHS?  
3. Policy preprocess vs full lower for date ops?  
4. Host clock surface for tests (injectable) vs wall clock?  
5. What stays pack-only (business days, TZ)?  

## Spike outputs (when research runs)

- [ ] Short design lock doc (or ADR stub) with chosen style A/B/C/D  
- [ ] Inventory of existing `DateOperation` / `now` / builtins in tree  
- [ ] One spike sketch (optional file-based app or failing test list) — **not** product merge  
- [ ] Decision: solidify `p1-*` suite or defer  

## Do not

- Parallel P1 with P3/P2 CURRENT  
- Grammar re-base as prereq  
- Ship incomplete `schedule at` as domain truth without host adapter  

## Related product next

| Order | Suite |
|-------|--------|
| 1 | [`simple-agent-tasks/p3-README.md`](simple-agent-tasks/p3-README.md) — return types |
| 2 | [`simple-agent-tasks/p2-README.md`](simple-agent-tasks/p2-README.md) — multi-hop |
| later | P1 after this research |
