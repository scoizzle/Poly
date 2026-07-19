# Micro-Task: Q1.3 — Parse/print/lower postfix `Rel exists`

**Suite:** [`qe-README.md`](qe-README.md) **#Q1.3**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md) §4.0 exists  
**Difficulty:** Small–Medium  
**Estimated Context:** ~10k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** Q1.1 `[x]`; Q1.2 preferred (shared Rel primary)

## Objective

```poly
assignee exists
not certificate exists
```

→ `Exists` / `NotExists` DE → existing lower → printer round-trip.  
**Not:** `exists assignee`, `certificate not exists`.

## Required Reading

- Q1.1 + Q1.2 path-prefix plumbing
- `DomainExpression.Exists` / `NotExists`
- Lowering Exists/NotExists cases
- Keyword registration pattern (e.g. `delete`)

## Exact Steps

1. Tokenize/parse `Rel exists` as related_simple / primary.
2. Absence = outer `not` + `Rel exists` (existing Not node or NotExists — match Q1.1).
3. Printer: `Rel exists` / `not Rel exists` only.
4. Tests: happy + fail-loud for prefix `exists Rel` if still a parse path.
5. Many-side `orders exists` per Q1.1 decision.
6. Boolean `exists` is **not** valid assign RHS.

## Verification

- [ ] Postfix forms green
- [ ] Prefix exists not product
- [ ] Build + tests green

## Output

- Parser/printer/tests
- Summary: `../agent-summaries/qe-q1-3-summary.md`

## Out of Scope

- `where` rebind (Q1.3b)
- any/all
- Guide (Q1.6)

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
