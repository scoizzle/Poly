# Micro-Task: Q1.3b — Parse/print/lower to-one `Rel where` rebind

**Suite:** [`qe-README.md`](qe-README.md) **#Q1.3b**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md) §4.0 where  
**Difficulty:** Medium  
**Estimated Context:** ~12k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** Q1.2 + Q1.3 `[x]`

## Objective

```poly
customer where Status is "Active" and Tier is "VIP"
```

No forced parentheses around the body. Body binds to **related** subject (bare props). To-one / owned only for this task (not `any Rel where` — Q3′).

## Required Reading

- Q1.1 where-body rule (and-chain vs or)
- Path-prefix + exists parser state
- Lowering / nav IR reuse

## Exact Steps

1. Parse `Rel where <body>` as primary; body per Q1.1 (prefer and-chain; paren for or).
2. Rebind subject for body property resolution.
3. Printer: no forced parens; round-trip.
4. Tests: multi-pred to-one; outer `and` with quantified/scoped primary.
5. Fail-loud: `where` on `many` without `any`/`all` (if Q1.1 says so).

## Verification

- [ ] Ticket `ActiveVip`-style policy green
- [ ] No forced `where (…)`
- [ ] Build + tests green

## Output

- Parser/printer/tests
- Summary: `../agent-summaries/qe-q1-3b-summary.md`

## Out of Scope

- `any`/`all`/`count` (Q3′)
- Guide (Q1.6)

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
