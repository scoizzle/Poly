# Micro-Task: Confirm Q3′ quantifier keyword set

**Suite:** [`qe-README.md`](qe-README.md) **#Q0.4**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md) §4.0 · §5 Q0.4  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~4k tokens  
**Status:** `[ ]` Not Started  

## Objective

Confirm (or adjust with rationale) the **frozen lean** for collection quantifiers: **keyword** forms  
`any` / `all` / `none` / `count` **Rel where …** — **not** C# method chains (`orders.any(...)`, `.Any()`).

## Required Reading

- `dsl-query-surface.md` §4.0 Q-linq + decision log
- `PolyDslTokenizer.cs` — collisions with `any`, `all`, `count`, `none`, `exists`, `where`
- Guide reserved / unsupported keywords

## Exact Steps

1. Check tokenizer collisions for those keywords (contextual after RelName is OK to note).
2. Confirm product forms:

```text
any orders where Status is "Open"
all lineItems where Reserved is true
none notes where NeedsFollowUp is true
count orders where Total > 0 >= 1
```

3. Decision log: confirm **keyword + where** (reject method syntax as primary).
4. No parser work; Q3′ not started.

## Verification

- [ ] Decision log row (confirm or justified change)
- [ ] No `orders.any(o => …)` as product goal
- [ ] No code changes

## Output

- Decision log update
- Summary: `../agent-summaries/qe-q0-4-summary.md`

## Out of Scope

- Implementing any/all
- Lambda binders

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
