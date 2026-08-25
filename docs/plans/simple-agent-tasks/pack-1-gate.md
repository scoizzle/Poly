# pack-1-gate — Phase 1 done

**Difficulty:** S  
**Status:** `[x]`  
**Prereq:** pack-1-1 … pack-1-4 `[x]`  

## Objective

Phase 1 locks hold. pr1 on dirty phase-1 files.

## Exact steps

1. Confirm `IExpressionPrintForm` does not exist.
2. Confirm `ExpressionPrinter.Default` throws (no `?`).
3. Confirm `DslTokenWriter` produces `Order: entity`.
4. `git diff --stat HEAD` then pr1 categories on pack-1 files only.
5. Full build + suite.

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

- [x] 🔴🟠 none  
- [x] Suite green  
- [x] Mark [`pack-1-README.md`](./pack-1-README.md) Done  

## File ownership

| Edit | Do not edit |
|------|-------------|
| task status checkboxes / pack-1-README | new production features |

## Status

**Status:** Done  
**Claimed by:** opencode (pack-1-gate) 2026-08-13
