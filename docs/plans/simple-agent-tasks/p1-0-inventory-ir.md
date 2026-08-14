# p1-0 — Inventory existing temporal IR

**Difficulty:** S  
**Status:** `[x]`  

## Objective

Document what already exists. **No product feature code.**

## Exact steps

1. Create `docs/plans/simple-agent-tasks/p1-inventory-notes.md`.

2. Fill table by reading code (paths from design lock):

| Asset | File | Exists? | Notes |
|-------|------|---------|-------|
| `DateOperation` / `DateOperationKind` | `DomainExpression.cs` | | |
| Lowering AddDays/Months | `DomainExpressionLoweringPass.cs` | | |
| `default now` strings | EffectLoweringPass | | |
| `Now` as DomainExpression node | | | |
| ExpressionFormRegistry | Parsing/ | | E1 |

3. List gaps for tasks 1–4 only (not TZ/schedule).

## Verification

- [x] Notes file complete  
- [x] No production code changes  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `p1-inventory-notes.md` | `Poly/**` code |

## Status

Claimed by: opencode (p1-0-inventory-ir)

**Status:** Done  
