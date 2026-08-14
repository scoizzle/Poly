# p1-4 — Analysis fail-closed temporal rules

**Difficulty:** M  
**Status:** `[x]`  
**Prereq:** task 3  
**Claimed by:** p1-4 fleet agent  

## Objective

Analysis (or parse) rejects illegal temporal use: unknown unit, bad type pairs, unresolved specialization.

## Exact steps

1. Implement checks (new small analyzer or extend existing):

   | Case | Result |
   |------|--------|
   | Unknown unit token used as duration | Error, no vacuous success |
   | `Date + Date` if produced | Error |
   | `Number + days` without temporal lhs if meaningless | Error |
   | Unresolved specialization | Error / throw at lower — fail loud |

2. Opt-out: inputs **without** temporal pack — `Now` must not lower as clock (PropertyAccess or error). Test pack-absent path.

3. Tests named after design-lock negatives.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [x] Negative tests green  
- [x] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Analysis/*` as needed | MCP catalog |

## Status

**Status:** Done  
**Claimed by:** p1-4 fleet agent  
**Notes:** Analysis fail-closed temporal rules in `ExpressionTypeAnalyzer` (new `Duration` type category + `Now`/`Today` type as Date). Unknown units fail closed at parse (form only matches known spellings; leftover unit token fails parse). `Date + Date` and bare durations without a temporal left operand (`Number + days`, assign `3 days`, `default(3 days)`) are rejected at analysis; a surviving `Duration` also throws at lowering (fail loud). `date + duration` with a temporal left operand stays analysis-valid (resolves later). Pack-absent: `Now` parses as `PropertyAccess` and lowers as a member — never a clock — and policy references to it are unknown-property errors. No parser forms, no lowering date arms, no printer files edited.
