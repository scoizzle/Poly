# gpure-8 — CORE + docs honesty

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** task 7  

## Objective

Document pure Grammar product path. No behavior change.

## Exact steps

1. Update `docs/CORE.md` placement: Grammar owns engine; DomainModeling owns **product grammar tables + handlers** (not dual RD language).  
2. Update `Poly/DomainModeling/README.md` Parsing row: pure Grammar-driven.  
3. Update `Poly/Grammar/README.md` if API surface changed (Rule, LeftAssoc).  
4. Parent [`../grammar-pure-end-state.md`](../grammar-pure-end-state.md) §8 checkboxes → tick completed items.  
5. `READY-TO-TASK.md`: mark gpure status if complete after gate.  
6. Guide (`poly-dsl-guide.md`): only if error messages/syntax claims changed — same change rule.

## Verification

- [ ] Docs match code  
- [ ] Suite still green  

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

## File ownership

| Edit | Do not edit |
|------|-------------|
| CORE, DomainModeling README, Grammar README, parent plan checkboxes | Reintroduce RD |

## Status

**Status:** Not Started  
