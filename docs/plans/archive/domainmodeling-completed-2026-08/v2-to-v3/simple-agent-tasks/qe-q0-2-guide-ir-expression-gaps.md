# Micro-Task: Document IR gaps + planned subject-first surface

**Suite:** [`qe-README.md`](qe-README.md) **#Q0.2**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md) §3.1 · §4.0 · §5 Q0.2  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~7k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** Q0.1 `[x]` (or same PR as Q0.1)

## Objective

Guide (and/or query plan pointer) must: (1) name DE nodes that lower but are not authorable in DSL today; (2) name the **planned** product spellings so agents do not invent dots or C# LINQ — without claiming they ship yet.

## Required Reading

- `Poly/DomainModeling/DomainExpression.cs`
- `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs`
- `Poly.Mcp/Docs/poly-dsl-agent-guide.md` — after Q0.1
- `dsl-query-surface.md` §3.1 + §4.0 (**frozen** planned forms)

## Planned spellings to name (as “coming”, not “supported”)

| Planned | Not planned |
|---------|-------------|
| `assignee exists` / `not certificate exists` | `exists assignee`, `certificate not exists` |
| `customer Tier is "VIP"`, `assignee Active` | `customer.Tier`, `customer->Tier` |
| `customer where Status is "Active" and …` | forced `where (…)` only dialect |
| Later: `any orders where Status is "Open"` | `orders.Any(o => …)` |
| Assign: local target; scalar related **read** RHS | `assign customer Status to …` |

## Exact Steps

1. Table: DE node | lowers? | DSL today? | planned DSL form.
2. Guide section: **Expression IR / planned related reads** (mirror effect library-only honesty).
3. One sentence: **cross-entity reads planned; cross-entity writes banned** (assign targets this entity only).
4. Point Q1′ vs Q3′; do **not** implement parser.

## Verification

- [ ] Major DE factories classified
- [ ] Guide does not claim path-prefix/`Rel exists` ship today
- [ ] Planned forms match §4.0 (no dots)
- [ ] Docs-only preferred

## Output

- Guide (+ optional plan note)
- Summary: `../agent-summaries/qe-q0-2-summary.md`

## Out of Scope

- Q1′ implementation
- Full matrix (Q0.3)

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
