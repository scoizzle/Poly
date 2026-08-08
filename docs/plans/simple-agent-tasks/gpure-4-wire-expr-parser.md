# gpure-4 — Wire product expression parse to Grammar

**Difficulty:** M–L  
**Status:** `[ ]`  
**Prereq:** task 3  

## Objective

`DslExpressionParser.ParseExpression` (or replacement) builds `DomainExpression` **from Grammar matches**, not from private `ParseOr`/`ParseAnd` while-loops. Residual handler logic only for quantifier/path-prefix if still needed.

## Required reading

1. `DslExpressionParser.cs`  
2. `DslGrammar` expr rules from task 3  
3. How `MatchRule` + `Unread` dual-cursor works in `PolyDslParser`  

## Exact steps

1. Implement parse that:
   - Uses Matcher on `expr` (top rule) at cursor (respect dual-cursor: Unread head if needed).  
   - On match, **Consume** tokens and fold tokens/pattern names into `DomainExpression` **or** recursively: match layer rules and build IR in handlers.  

2. **Recommended approach (pick one, document in notes):**

   - **A. Recursive descent over rules using only MatchRule/Consume** (no while on raw kinds for +/*) — control flow still in code but **guided by table**.  
   - **B. Single MatchRule("expr")** then interpret token list + op positions into IR.  

   Prefer **A** if LeftAssoc only yields flat tokens (easier fold in layer methods that call MatchRule for operands).

3. **Must preserve** semantics of existing expression tests:
   - comparisons, and/or, arithmetic, quantifiers, path-prefix, multi-hop, exists, where  

4. Dual-run temporary (allowed): if needed for safety, compare old RD vs new on a corpus then delete old within this task. Prefer delete old ParseOr/ParseAnd bodies in same PR once green.

5. Keep `IExpressionPrimaryForm` hook **before** primary match (bridge for temporal).

6. Tests: full suite is the bar; add 1–2 dual-run goldens if useful:

   - `Expr_GrammarPath_AgeGte18_Policy`  
   - `Expr_GrammarPath_ArithmeticInPolicy`  

## Verification

```bash
dotnet build Poly.Tests/Poly.Tests.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] No `while (_current.Kind == Plus)` style loops left in expression parser (grep)  
- [ ] Full suite green  

```bash
rg -n "ParseOr|ParseAdd|while \(_c\.Current\.Kind == TokenKind\.Plus" Poly/DomainModeling/Parsing/DslExpressionParser.cs
# Expect: no old-layer methods (or file replaced)
```

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Parsing/DslExpressionParser.cs` | Effect ParseEffect RD (task 5) |
| `PolyDslParser.cs` only if wiring requires | Grammar engine (unless bugfix) |
| Tests | MCP minify |

## Status

**Status:** Not Started  
