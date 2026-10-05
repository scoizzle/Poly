# e2e-r-5 — if-conditions + runtime-keyword assign

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** r-4  
**Fleet:** P2-3, P2-5, P2-6  

## Objective

- `assign Qty to now` rejected at analysis (mirror `CheckDefault` keyword rules).  
- `if (Qty)` rejected (non-boolean).  
- `if (Genre is Fiction)` in action bodies agrees with policy enum-member resolution.

## Exact steps

1. Three failing tests: `Assign_NowToNumber_AnalysisRejects`, `If_NonBoolean_AnalysisRejects`, `If_BareEnumMember_ResolvesLikePolicy`.
2. Smallest checks in `ExpressionTypeAnalyzer` / assign validation. Do not add a new pass.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `ExpressionTypeAnalyzer.cs` + existing assign/default checkers | `DslExpressionParser` |
| tests | |

## Status

**Status:** Not Started  
**Claimed by:**  
