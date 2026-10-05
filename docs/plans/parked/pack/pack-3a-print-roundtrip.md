# pack-3a-print-roundtrip — Temporal spelling survives export_dsl

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** p1-3 pack registration `[x]`  
**Slice:** p1 / phase 3a  

## Objective

`Now - 12 days`, `DueDate + 14 days`, `ExpiryDate < Now` print through Grammar binder + DslTokenWriter and re-apply.

## Exact steps

1. Failing tests: print a domain that contains those expressions; reparse; IR equivalent.
2. Temporal pack registers patterns on both primaries **and** print binders for `Now` / `Today` / `DateOperation`.
3. Session **without** temporal pack: parse rejects (or analysis fails) **and** print of `DateOperation` throws.
4. No `IExpressionPrimaryForm` unless you cite an engine gap on the task file.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*Temporal*"
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*PolyDslRoundTrip*"
```

- [x] Round-trip goldens  
- [x] Missing pack fails closed both ways  

## File ownership

| Edit | Do not edit |
|------|-------------|
| temporal pack project / forms / binders | `MinimalApiGenerator.cs` |
| p1 tests | OpenAPI |

## Status

**Status:** Done  
**Claimed by:** fleet agent pack-3a (opencode) — 2026-08-13  
**Notes:** 
- Temporal pack now registers Grammar patterns (`now`/`today`/`duration`) on both primaries and print binders for `Now`/`Today`/`DateOperation`; DateOperation prints via a dedicated `date-operation` rule (add/sub by offset sign) through DslTokenWriter.
- Extended the parse fold (DslExpressionParser.TryFoldDateOperation) so a date property (`DueDate + 14 days`) also folds to a DateOperation — required for the objective; a bare duration still fails analysis (p1-4 owns that).
- Pack-absent fails closed both ways: `Now - 12 days` rejects without the pack; printing a DateOperation without the pack throws.
- No `IExpressionPrimaryForm` added — the existing Now/Duration forms keep the cited-gap RD fold (pack-host lock 13). No DomainDslPrinter change needed (binders are registered, not hard-coded).
- Guide (`poly-dsl-guide.md`) intentionally NOT edited per task file ownership; it already defers date-operation authoring to the p1 temporal plan.  
