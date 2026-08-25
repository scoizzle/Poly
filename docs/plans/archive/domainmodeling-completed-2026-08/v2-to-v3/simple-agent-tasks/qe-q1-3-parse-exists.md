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
2. **DE (Q1′′.1):** `Exists(PropertyAccess(relName))` — **not** `RelationshipNavigation` with empty property. Absence: prefer `NotExists(PropertyAccess(relName))` or parse `not Rel exists` as `Not(Exists(...))` with round-trip that still prints `not Rel exists`.
3. Printer: `Rel exists` / `not Rel exists` only.
4. Tests: happy + fail-loud for prefix `exists Rel` if still a parse path; assert DE shape for exists.
5. Many-side `orders exists` allowed (non-empty) per §4.5.0.
6. Boolean `exists` is **not** valid assign RHS.
7. Soft-miss for missing to-one is for path-prefix compares; exists false when unlinked.

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
