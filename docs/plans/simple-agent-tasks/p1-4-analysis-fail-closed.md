# p1-4 — Analysis fail-closed temporal rules

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** task 3  

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

- [ ] Negative tests green  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Analysis/*` as needed | MCP catalog |

## Status

**Status:** Not Started  
