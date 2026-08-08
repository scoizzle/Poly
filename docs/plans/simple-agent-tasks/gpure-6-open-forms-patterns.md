# gpure-6 — Open forms via patterns (where possible)

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** task 4  

## Objective

Reduce reliance on opaque RD `IExpressionPrimaryForm` for shapes that tables can express. Prefer grammar patterns + thin handlers.

## Exact steps

1. Inventory all `IExpressionPrimaryForm` implementations (grep). Today may be **only tests** (`MAGIC`) — if only tests, document “no product forms yet” and:

   - Add a **product-shaped example** in Grammar tests: pattern for `Number` + Identifier unit-like, handler not required if only match test.  
   - Document how temporal p1 should register: `ContributeGrammarPatterns` + optional form only if engine still insufficient.

2. If product forms exist: migrate each to patterns on `expr-primary` (or pack contributor) where possible.

3. Ensure `ExpressionFormRegistry.ContributeGrammarPatterns` still runs at `DslGrammar.Build`.

4. Update parent notes: which forms remain RD and **why** (engine gap) — if any residual RD form, must cite missing engine feature.

5. Tests: MAGIC form still works **or** replaced with pattern-based equivalent test.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Notes updated  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| Parsing forms, DslGrammar, tests | Full p1 temporal product |

## Status

**Status:** Not Started  
