# Dogfood S1 — Library checkout (lifecycle kernel)

**Queue:** [`dogfood-README.md`](dogfood-README.md)  
**Protocol:** [`../mcp-dogfood-protocol.md`](../mcp-dogfood-protocol.md)  
**Status:** `[x]` PASS -- [re-run2](../agent-summaries/dogfood/DOGFOOD-S1-RERUN2-20260725.md)  
**Difficulty:** Medium  
**Est. session time:** 30–60 min agent time  

---

## Goal

Author and **run** a small library domain end-to-end on MCP: patron checks out a book via **create-in**, lifecycle stages, a **require** policy, and policy evaluation on a real instance.

## Concept under test

**Baseline product path** — stages, actions, policies, create-in, store-linked evaluation, subscriptions optional.

If S1 fails, higher concepts (link, owned) are premature.

---

## Domain sketch (minimum)

You may rename identifiers, but must preserve structure:

- **Book** — catalog entity (no borrow stages on Book).  
- **Patron** — may have stages or simple Active; policy e.g. not suspended.  
- **Loan** — transactional record with stages e.g. Active → Returned; created via `create in` from Patron.  
- Checkout action on Patron takes a Book (or creates Loan holding book ref per your design).  
- At least one **policy** used with `require` on checkout or return.  
- At least one **quantifier or path-prefix** in a policy or assign RHS (proves Q1′/Q3′ still usable).

Prefer principles in guide §0 (relationships not strings; lifecycle on Loan not Book).

---

## Success checklist

| # | Criterion | Met? |
|---|-----------|------|
| 1 | Domain applied via MCP (`apply_dsl` and/or evolve) with **zero analysis errors** | |
| 2 | `export_dsl` round-trips or re-apply does not lose stages/actions/policies of interest | |
| 3 | Create Patron + Book instances; create Loan via **action** that uses `create in` (or equivalent product path) | |
| 4 | Invoke at least one lifecycle action on Loan (e.g. return / transition) successfully | |
| 5 | `evaluate_policy` with `instanceId` (or documented store-linked path) returns a meaningful true/false on a real instance | |
| 6 | `get_domain_analysis` usable (errors empty on happy model; structured facts if present) | |

**PASS** = all six yes without forbidden workarounds.

---

## Forbidden workarounds

- Putting checkout stages on **Book** instead of Loan  
- Skipping runtime and only exporting DSL  
- Using library C# APIs instead of MCP runtime tools for the happy path  
- Dropping the policy/quantifier requirement to “make it green”

---

## Session steps (suggested)

1. `create_domain_session`  
2. `get_dsl_guide` — note anything unclear for report  
3. `apply_dsl` full domain (or build incrementally if batch fails — record why)  
4. `get_domain_analysis` / suggestions  
5. `create_instance` Book, Patron  
6. `invoke_action` checkout → Loan  
7. `link_instances` only if your design requires it (S1 should prefer create-in)  
8. `invoke_action` on Loan lifecycle  
9. `evaluate_policy` with instanceId  
10. Write report; stop  

---

## If blocked

Fill protocol template; bucket the failure. Do not start S2 in this turn.

---

## Report output

`docs/plans/v2-to-v3/agent-summaries/dogfood/DOGFOOD-S1-YYYYMMDD.md`

---

## Status tracking

**Claimed by:** agent 2026-07-25  
**Started:** 2026-07-25  
**Report path:** `agent-summaries/dogfood/DOGFOOD-S1-20260725.md` · mutation `DOGFOOD-S1-MUTATION-FINDINGS-20260725.md`  
**Result:** PARTIAL  

