# p1-6 — DSL guide honesty

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** task 5  

## Objective

Update `Poly.Mcp/Docs/poly-dsl-guide.md` so temporal vertical is documented as **shipped** with limits; remove or narrow "Not yet shipped: Date operations" if now false.

## Exact steps

1. Document: `Now`, `today` (if shipped), `N days` / `N months`, assign + policy compare.  
2. Document fail-closed: unknown units; pack-absent if applicable.  
3. Explicitly **not** shipped: schedule at, business days, TZ.  
4. Ensure `GetDslGuide_ReturnsProductSurface` / guide smoke still passes.  
5. No lab experiment grammar as product.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Guide matches behavior  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Mcp/Docs/poly-dsl-guide.md` | Experiment docs as product |

## Status

**Status:** Not Started  
