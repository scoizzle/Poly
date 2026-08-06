# Micro-Task: Expression parity matrix (DE × DSL × JSON × lower × VM)

**Suite:** [`qe-README.md`](qe-README.md) **#Q0.3**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md) §5 Q0.3 · §4.0  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~8k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** Q0.1–Q0.2 ideally done

## Objective

Durable **expression parity matrix** with legend (✅ 🟡 ❌ 🚫): one row per DomainExpression kind; columns DSL (today) / planned DSL form / JSON / lower / eval-or-VM. Mark Q1′ vs Q3′.

## Required Reading

- `Poly/DomainModeling/DomainExpression.cs`
- `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs`
- JSON policy path / `DomainExpressionJsonParser` if present
- Effect matrix style: `effect-surface-completeness.md` §2
- Planned forms: `dsl-query-surface.md` §4.0

## Exact Steps

1. List DE record types from `DomainExpression.cs`.
2. Mark honesty columns; **planned DSL** uses subject-first spellings only.
3. Write matrix into `dsl-query-surface.md`.
4. Note assign: LHS never related; RHS scalar related = read (Q1′).

## Verification

- [ ] No overstate of DSL or JSON
- [ ] Lower claims match lowering pass
- [ ] No product dots in “planned” column

## Output

- Updated `dsl-query-surface.md` matrix
- Summary: `../agent-summaries/qe-q0-3-summary.md`

## Out of Scope

- Parser implementation
- Re-opening surface dialect

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
