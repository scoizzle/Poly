# pack-1-2 — Expression print binder registry

**Difficulty:** M  
**Status:** `[x]`  
**Wave:** A · **Parallel with:** pack-1-1  

## Objective

IR → `(rule, pattern, fills)` is a registered binder. Duplicate IR owner fails closed. No string-concat print API.

## Exact steps

1. Failing tests first (new file `Poly.Tests/DomainModeling/Parsing/ExpressionPrintBinderTests.cs`):
   - `Register_DuplicateOwner_Throws`
   - `TryBind_UnknownExpression_ReturnsFalse` (or throws — pick **false** here; pack-1-3 makes DomainDslPrinter throw on miss)
   - `TryBind_Registered_ReturnsRuleAndPattern`
2. `[NEW]` types under `Poly/DomainModeling/Parsing/` (name for what they are):
   ```csharp
   public readonly record struct PrintBinding(string Rule, string Pattern);

   public interface IExpressionPrintBinder {
       bool TryBind(DomainExpression expression, out PrintBinding binding);
   }

   public sealed class ExpressionPrintRegistry {
       public void Register(IExpressionPrintBinder binder);
       public bool TryBind(DomainExpression expression, out PrintBinding binding);
   }
   ```
   First matching binder wins. `Register` that would claim the same concrete expression type as an already-registered binder → `InvalidOperationException`.
3. Do **not** add `IExpressionPrintForm` / `TryPrint(out string)`.
4. Do **not** edit `Printer.cs` or `DomainDslPrinter.cs`.
5. Optional: one sample binder in tests that maps `Literal` bool true → `("expr-primary", "true")`.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*ExpressionPrintBinder*"
```

- [x] Duplicate owner fails closed  
- [x] Unknown returns false  
- [x] Suite still builds  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `[NEW] Poly/DomainModeling/Parsing/ExpressionPrintBinder.cs` (or split types) | `Printer.cs` |
| `[NEW] Poly.Tests/DomainModeling/Parsing/ExpressionPrintBinderTests.cs` | `DomainDslPrinter.cs` |
| | `ExpressionFormRegistry.cs` |

## Status

**Status:** Done — opencode (pack-1-2-print-binder) 2026-08-13

Claimed by: opencode (pack-1-2-print-binder)
