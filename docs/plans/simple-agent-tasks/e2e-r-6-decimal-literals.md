# e2e-r-6 — Decimal literals are Number

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** r-0 (parser-only; can run after r-5)  
**Fleet:** P2-7  

## Objective

`Total * 0.9` and `default(0.5)` parse as Number. Not p1. Guide §8 claims this.

## Exact steps

1. Failing tests: `Parse_DecimalLiteral_IsNumber`, `Default_Decimal_TypesAsNumber`.
2. `DslExpressionParser.ParsePrimary` numeric fallback for decimals (only parser change allowed in e2e-r).
3. Do not add `days` / `Now` forms.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Parsing/DslExpressionParser.cs` | DateOperation / p1 |
| tests | |

## Status

**Status:** Not Started  
**Claimed by:**  
