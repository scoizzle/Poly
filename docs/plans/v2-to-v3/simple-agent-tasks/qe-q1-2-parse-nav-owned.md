# Micro-Task: Q1.2 — Parse/print/lower path-prefix

**Suite:** [`qe-README.md`](qe-README.md) **#Q1.2**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md) §4.0 path-prefix · §3.1  
**Difficulty:** Medium  
**Estimated Context:** ~12k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** Q1.1 `[x]`

## Objective

Product DSL authors **path-prefix** related reads:

```poly
assignee Active
customer Tier is "VIP"
customer Status is "Active"
```

Round-trip printer; lower via existing `RelationshipNavigation` / `OwnedAccess` (no new VM opcodes).  
**Scalar** path-prefix OK on **assign RHS**; **reject** multi-token / nav-shaped **assign LHS**.

## Required Reading

- Q1.1 spec
- `PolyDslParser.cs` / `DomainDslPrinter.cs` / tokenizer
- `DomainExpression.cs` + `DomainExpressionLoweringPass.cs`
- AGENTS.md TUnit conventions

## Exact Steps

1. Parse path-prefix per Q1.1 (to-one / owned only for props).
2. Printer round-trip (subject-first; **never** print dots).
3. Tests: parse → DE shape; print → parse; optional lower smoke.
4. Assign: `assign Label to customer Tier` OK if scalar; `assign customer Status to "X"` fail-loud.
5. Fail-loud: `many` + property without quantifier (e.g. `orders Status is "Open"`).
6. Update matrix if present.

## Verification

- [ ] Build + new tests green
- [ ] No product dots
- [ ] Cross-entity write via assign rejected
- [ ] No Q3′ quantifiers

## Output

- Parser/printer (+ analysis if needed), tests
- Summary: `../agent-summaries/qe-q1-2-summary.md`

## Out of Scope

- `Rel exists` (Q1.3)
- `Rel where` rebind (Q1.3b)
- JSON (Q1.5)
- Guide polish (Q1.6)

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
