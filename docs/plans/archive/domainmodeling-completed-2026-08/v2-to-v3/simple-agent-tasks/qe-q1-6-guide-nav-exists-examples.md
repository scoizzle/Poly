# Micro-Task: Q1.6 — Guide examples + read/write rule

**Suite:** [`qe-README.md`](qe-README.md) **#Q1.6**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md) §5 Q1.6 · §3.1  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~6k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** Q1.4 `[x]`

## Objective

Product guide documents **shipped** subject-first related reads with copy-paste examples that match goldens. State **cross-entity reads legal; cross-entity writes banned**. Rebuild `get_dsl_guide` embed.

## Required Reading

- `poly-dsl-agent-guide.md` (Q0 expression sections)
- Q1.1 syntax + Q1.4 goldens
- `dsl-query-surface.md` §3.1

## Exact Steps

1. Move path-prefix / `Rel exists` / to-one `where` from “planned” to **supported** (only what actually ships).
2. Examples:

```poly
assignee exists
not certificate exists
assignee Active
customer Tier is "VIP"
customer where Status is "Active" and Tier is "VIP"
```

3. Explicit **Do not**: `customer.Status`, `exists assignee`, `assign customer Status to …`.
4. Assign: local target; scalar related RHS OK if shipped.
5. Update query plan checklists + agent pick → post-Q1′.
6. Embed rebuild path.

## Verification

- [ ] Examples match green parser
- [ ] No Q3′ overclaim
- [ ] §3.1 stated in guide
- [ ] Build green

## Output

- Guide + plan pick updates
- Summary: `../agent-summaries/qe-q1-6-summary.md`

## Out of Scope

- New parser features
- E3 invoke

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
