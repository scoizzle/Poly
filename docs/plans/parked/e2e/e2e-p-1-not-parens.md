# e2e-p-1 — `not (…)` parentheses

**Difficulty:** S  
**Status:** `[ ]`  
**Fleet:** P4-1  

## Objective

`not (Total > 0)` round-trips. Printer `Not` currently emits `not {operand}` without parens (`DomainDslPrinter.cs` ExpressionPrinter.Not). And/Or already parenthesize.

## Exact steps

1. Failing test: parse `not (Total > 0)`, print, parse print → same comparison under Not. Name: `Print_NotComparison_RoundTripsWithParens`.
2. Parenthesize a non-atomic operand in `Not` (same style as And/Or).
3. Do not change And/Or/Add.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --filter Print_NotComparison_RoundTripsWithParens
```

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Parsing/DomainDslPrinter.cs` | parser grammar |
| `Poly.Tests/DomainModeling/Parsing/**` | guide |

## Status

**Status:** Not Started  
**Claimed by:**  
