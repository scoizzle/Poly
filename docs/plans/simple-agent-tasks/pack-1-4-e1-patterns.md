# pack-1-4 — E1 MAGIC / N unit as grammar patterns + round-trip

**Difficulty:** M  
**Status:** `[x]`  
**Wave:** B · **Prereq:** pack-1-3 `[x]`  

## Objective

Pack-shaped open forms are **patterns + fold + binder**, not RD `IExpressionPrimaryForm`. They parse, print, and reparse.

## Exact steps

1. Failing tests in `DslExpressionE1Tests` (replace or extend MAGIC / duration):
   - Register patterns on **both** `expr-primary` and `expr-primary-no-not`.
   - `OpenForm_MagicIdentifier_ParsesAndPrintsAndReparses`
   - `OpenForm_NumberUnit_ParsesAndPrintsAndReparses` (e.g. `12 days` as the pack spelling; IR may stay a `Literal` or a small test node — do **not** ship product temporal IR here).
   - `WithoutPattern_MagicIsPropertyAccess` still holds.
2. Pattern registration via `DslGrammar.Build` configure / `ContributeGrammarPatterns`. Fold in the product expr parser when the pattern name matches (thin handler — not a new RD primary form). If the engine cannot fold without RD, cite the gap in the test comment **and** still register the print binder.
3. Print binder for the MAGIC IR (Literal 42 or dedicated test node) emits the pattern `MAGIC` identifier — not `42` — so reparse hits the form again. If that conflicts with Literal binders, use a tiny test-only expression node in the test assembly **or** a pack-owned marker type in DomainModeling only if tests cannot otherwise round-trip. Prefer: MAGIC prints as identifier `MAGIC` via a binder that matches Literal(42) **only when that pack is loaded** (pack-scoped binder).
4. Stop advertising `IExpressionPrimaryForm` as the product API in comments on `ExpressionFormRegistry`. Leave the type if tests still need the escape; new product forms must not use it.
5. No `Now` / `months` product spelling.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*DslExpressionE1*"
```

- [x] MAGIC and N-unit round-trip  
- [x] No new `IExpressionPrintForm`  
- [x] Full suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Tests/Grammar/DslExpressionE1Tests.cs` | `Printer.cs` |
| `ExpressionFormRegistry.cs` (comments / contribute only) | `DomainDslPrinter.cs` walk logic |
| `DslExpressionParser.cs` (thin pattern-name fold only) | `DslCompiler.cs` |
| `DslGrammar.cs` only if a helper is required | temporal product |

## Status

**Status:** Done  
**Claimed by:** opencode (pack-1-4-e1-patterns)  
