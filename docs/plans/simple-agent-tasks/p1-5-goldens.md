# p1-5 — Product goldens (design-lock appendix)

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** tasks 1–4  

## Objective

End-to-end goldens matching design-lock appendix (implement as real tests, not sketches).

## Exact steps

Add tests (class name flexible) that prove:

1. **`Now_Minus_12Days_AssignsToDateProperty`**  
   - Domain with Date property; action/effect `assign DueDate to Now - 12 days` via DSL parse + evolve **or** fragment+effect construction.  
   - Prefer full `.poly` apply if possible.  
   - Assert IR or runtime/eval with fixed `TimeProvider` / clock.

2. **`ExpiryDate_LessThan_Now_Policy`**  
   - Policy `ExpiryDate < Now`; evaluate with fixed clock true/false.

3. Re-assert unknown unit + pack-absent from task 4 if not already e2e.

4. Use TUnit; no merge of sketch-only incomplete tests.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Appendix scenarios green  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Tests/**` | Unrelated production refactors |

## Status

**Status:** Not Started  
