# mut-safety-5 — Stage insertion order

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** task 0  

## Objective

After successful evolves, stage order for an entity matches **insertion order**, not alphabetical hash order. After a **failed** multi-change evolve, surviving stages keep prior order.

## Exact steps

1. Inspect how stages are stored on `Entity` and applied in evolution.  
2. If order is already stable in tests, add a regression test that adds stages `Active` then `Suspended` then `Closed` and asserts `get_entity_detail` / domain order is that sequence.  
3. If order is wrong, fix storage/merge to preserve order (e.g. list not unordered set) — **behavior-preserving for other cases**.  
4. Test name e.g. `Stages_PreserveInsertionOrder`.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Order test green  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| DomainModeling Entity/Evolution only if required | Unrelated refactors |
| `Poly.Tests/**` | |

## Status

**Status:** Not Started  
