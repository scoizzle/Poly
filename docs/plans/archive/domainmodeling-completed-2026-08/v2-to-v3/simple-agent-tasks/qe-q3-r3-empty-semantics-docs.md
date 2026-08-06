# Micro-Task: Q3′ residual — Empty-collection semantics in guide

**Suite:** [`qe-README.md`](qe-README.md) **#Q3.R3**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md)  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~4k tokens  
**Status:** `[ ]` Not Started  

## Objective

Document empty-link semantics next to Collection Quantifiers in the product guide (match runtime):

| Form | Empty related set |
|------|-------------------|
| `any` | **false** |
| `all` | **false** (no vacuous true — product choice) |
| `none` | **true** (¬any) |
| `count` | **0** |

## Required Reading

- `DomainEntityInstance.EvaluateAnyExpr` / `EvaluateAllExpr` (empty all → false)
- Guide Collection Quantifiers section
- Existing tests: `EvaluatePolicy_AllQuantifier_EmptySet_ReturnsFalse`

## Exact Steps

1. Confirm empty semantics from code (do not invent).
2. Add a short **Empty collections** note under Q3′ guide table.
3. Rebuild MCP embed if guide resource.

## Verification

- [ ] Guide matches code
- [ ] Build green

## Output

- Guide  
- Summary: `../agent-summaries/qe-q3-r3-summary.md`

## Out of Scope

- Changing empty-all to vacuous true (product decision already shipped)

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
