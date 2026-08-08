# gpure-7 — Delete RD language residual

**Difficulty:** M  
**Status:** `[x]`  
**Prereq:** tasks 4–5 (6 recommended)  

## Objective

**F2:** This is the **only** task that deletes the RD dual / leftover expression layer methods. gpure-4 leaves dual for parity; here dual dies.

Remove dead RD expression/effect language code. Live path = Grammar only. Handlers only.

## Exact steps

1. Delete `ParseExpressionRdForParity` / old `ParseOr`/`ParseAnd`/… if still present.

```bash
rg -n "ParseOr|ParseAnd|ParseMultiply|ParseAdd\(|ParseComparison|RdForParity" Poly/DomainModeling/Parsing --glob '*.cs'
```

2. Ensure `DslExpressionParser` is either deleted or **only** Matcher orchestration + IR fold + primary open-form hook — **no** kind-while arithmetic loops.

3. **Keep** `DslExprParityTests` but both sides must now be Grammar path vs **oracle snapshot** (serialized expected IR / second independent fold) **or** parity tests become single-path regression corpus.  
   - If dual RD is gone, convert parity suite to: Grammar parse vs **frozen expected** expression trees / round-trip through print if needed — do not leave tests calling deleted RD.

4. Effect: no alternate RD entry that bypasses `effect` rule.

5. Comments: remove “hybrid forever”.

6. Full suite green.

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

**Status:** Done 2026-08-07 — Rd dual deleted (`ParseExpressionRdForParity` + all `*Rd` layers); parity suite converted to frozen-IR oracles (Id-agnostic canonical); gate grep clean (exit 1); 1928 green.  
