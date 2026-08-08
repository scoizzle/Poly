# gpure-7 — Delete RD language residual

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** tasks 4–5 (6 recommended)  

## Objective

Remove dead RD expression/effect language code. Handlers only.

## Exact steps

1. Grep and eliminate leftover private expression layer methods if any:

```bash
rg -n "ParseOr|ParseAnd|ParseMultiply|ParseAdd\(|ParseComparison" Poly/DomainModeling/Parsing --glob '*.cs'
```

2. Ensure `DslExpressionParser` is either:
   - **Deleted** and call sites use a `GrammarExpressionParser` / methods on cursor, or  
   - Reduced to **only** Matcher orchestration + IR fold + primary open-form hook  

3. Effect: no alternate RD entry that bypasses `effect` rule.

4. Update any comments that say “hybrid forever” or “E2 permanent”.

5. Full suite green — **no** skipped corpus.

## Verification

```bash
rg -n "while \(_c\.Current\.Kind == TokenKind\.(Plus|Star|And)" Poly/DomainModeling/Parsing --glob '*.cs'
# Expect: empty (or only non-product test helpers)

dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Grep clean per above  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Parsing/**` | Unrelated modules |

## Status

**Status:** Not Started  
