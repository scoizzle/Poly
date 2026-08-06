# Micro-Task: Q1.1 — Spec residual (subject-first surface)

**Suite:** [`qe-README.md`](qe-README.md) **#Q1.1**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md) §3.1 · §4.0 · §5 Q1.1  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~8k tokens  
**Status:** `[x]` **Done** (`beeb922`) — formal spec in parent plan **§4.5**; open bits frozen in §4.5.0. Review follow-ups: parent **§10 Q1′′**.  
**Prereq:** **Slice Q0 exit**

## Objective

Write a **single** implementable spec (BNF or productions + examples → DE mapping) for the **already frozen** surface. Do **not** re-open dots, prefix exists, or C# methods.

## Already frozen (do not change)

| Topic | Choice |
|-------|--------|
| Path-prefix | `Rel BoolProp`, `Rel Prop op value`, `Rel Prop is [not] value` |
| Exists | `Rel exists` |
| Absence | `not Rel exists` |
| Where | `Rel where <body>` — no forced parens |
| Anti-dot | no `rel.Prop` / `rel->Prop` |
| Assign | LHS = this entity prop only; RHS may scalar-read related |
| Cross-entity | reads legal; writes banned |

## Open (must decide here)

| Topic | Recommended default |
|-------|---------------------|
| `where` body extent | **and-chain of comparisons**; `or` in body needs `(…)` |
| `orders exists` (many) | Allow as non-empty **or** fail-loud until Q3′ — pick one |
| Missing to-one on path-prefix | Soft **false** vs hard error — pick one + golden |
| Sticky vs repeat nav | `customer A and customer B` required for simples; rebind only under `where` |

## Required Reading

- `dsl-query-surface.md` §3.1 + §4.0 (full)
- `DomainExpression.cs` + `DomainExpressionLoweringPass.cs`
- Tokenizer keywords

## Exact Steps

1. Write short grammar productions for policy expressions + note assign RHS = scalar subset.
2. Map each Ticket sample → DE construction.
3. Record open decisions in §9 decision log.
4. Point Q1.2 / Q1.3 / Q1.3b at exact productions.
5. No parser code.

## Verification

- [ ] One dialect; matches frozen table
- [ ] Every form maps to DE (or explicit new IR only if unavoidable — prefer existing)
- [ ] Ambiguity + cardinality documented for implementers

## Output

- Spec section in query plan (or appendix)
- Summary: `../agent-summaries/qe-q1-1-summary.md`

## Out of Scope

- Q3′ any/all implementation
- Q1b params
- Parser code

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
