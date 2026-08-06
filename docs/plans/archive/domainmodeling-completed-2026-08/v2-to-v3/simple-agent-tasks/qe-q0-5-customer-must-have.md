# Micro-Task: Customer must-have expression list (product spellings)

**Suite:** [`qe-README.md`](qe-README.md) **#Q0.5**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md) §5 Q0.5 · §4.0  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~5k tokens  
**Status:** `[ ]` Not Started  

## Objective

Short ordered list of expression capabilities for 1–2 domains using **frozen product spellings** — map each to today / Q1′ / Q3′ / out of scope.

## Required Reading

- Dogfood reports (skim): `agent-summaries/dogfood/DOGFOOD-REPORT*.md`
- `dsl-query-surface.md` §3.1 · §4.0 Ticket/Order samples

## Exact Steps

1. Use Ticket and/or Order/Customer.
2. Write 5–12 **English** policy sentences, then the **product** form (or “not yet”).
3. Map: **today** | **Q1′** | **Q3′** | **out**.
4. Include at least: presence, absence, path-prefix compare, to-one multi (`where`), one any/all (Q3′), one banned cross-entity write.
5. Record in query plan.

## Example shape (do not invent dots)

```text
"Has an assignee" → assignee exists → Q1′
"No certificate" → not certificate exists → Q1′
"VIP customer" → customer Tier is "VIP" → Q1′
"Active VIP" → customer where Status is "Active" and Tier is "VIP" → Q1′
"Any open order" → any orders where Status is "Open" → Q3′
"Set customer's status" → BAN (cross-entity write)
```

## Verification

- [ ] Concrete sentences + product spellings
- [ ] Maps to slice IDs
- [ ] No full LINQ wishlist; no dots

## Output

- Query plan update
- Summary: `../agent-summaries/qe-q0-5-summary.md`

## Out of Scope

- Implementation

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
