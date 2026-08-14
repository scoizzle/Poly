# pack-1-3 — DomainDslPrinter uses binder + Printer

**Difficulty:** M  
**Status:** `[x]`  
**Wave:** B · **Prereq:** pack-1-1 `[x]` and pack-1-2 `[x]`  

## Objective

Expression print goes binder → Grammar `Printer` → `DslTokenWriter`. `?TypeName` is gone. Missing binder throws.

## Exact steps

1. Failing test first:
   - `PrintExpression_DateOperation_WithoutBinder_Throws` (construct a tiny domain/policy with `DateOperation` via fluent IR, print).
   - Existing expression print tests still match product spacing (`and`, `not`, literals).
2. `[MODIFY]` `DomainDslPrinter`:
   - Ctor takes `DomainParserInputs?` (or Grammar + `ExpressionPrintRegistry` + writer). Same inputs the parser would use.
   - `ExpressionPrinter`: before `Route`, `TryBind` → `Printer.Print(rule, pattern, fills)`. Nested children recurse.
   - `Default()` **throws** (`InvalidOperationException` naming the type). Delete `?{TypeName}`.
3. Register **core** binders for every expression subtype `ExpressionPrinter` currently handles (PropertyAccess, Literal, And, Or, Not, Add, …). Fills supply identifier/literal text only — writer owns spaces.
4. Core binders may print via existing dispatch **only** as a temporary fallback **if** a pattern does not exist for that construct — but `DateOperation` and unknown types must throw. Prefer table print for `expr-primary` literals/`true`/`false`/`null`/`ident` this task.
5. Do not add temporal spelling. Do not delete the domain-walk for entities/stages.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*DomainDslPrinter*"
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*PolyDslRoundTrip*"
```

- [x] `?DateOperation` gone — throws  
- [x] Round-trip tests still green  
- [x] Full suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Parsing/DomainDslPrinter.cs` | `Printer.cs` (unless 1-1 left a ctor hole — then only add overload) |
| `ExpressionPrintRegistry` registration of **core** binders (same file as 1-2 or new `CoreExpressionPrintBinders.cs`) | `DslCompiler.cs` |
| `Poly.Tests/DomainModeling/Parsing/**` print tests | MCP |

## Status

**Status:** Done  
**Claimed by:** opencode (pack-1-3-dsl-printer)  
