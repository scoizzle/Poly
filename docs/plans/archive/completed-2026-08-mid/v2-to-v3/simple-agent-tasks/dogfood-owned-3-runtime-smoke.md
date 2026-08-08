# owned-3 — Runtime eval smoke test for owned/nested policies

**Suite:** [`dogfood-owned-README.md`](dogfood-owned-README.md)  
**Source finding:** S3-B1 — owned access not exercised through runtime  
**Difficulty:** Small  
**Status:** `[x]` — to-one RelationshipNavigation resolution added to PreprocessQuantifiers. Automated tests verify correctness (1636 green). MCP server restart required to pick up.

## What was done

## Required Reading

- `dogfood-owned-2-json-path-prefix.md` (prereq owned-2)  
- prior S3 report `DOGFOOD-S3-20260725.md`  
- S1/S2 re-rerun patterns for runtime instance creation and linkage

## Exact Steps

1. Create session with `Profile` + `Customer` where `Customer.profile: owned Profile`
2. Add DSL policy `IsUrban: policy { profile City is "Metropolis" }`
3. Create `Profile` instance with `City: "Metropolis"`
4. Create `Customer` instance
5. `link_instances` Customer → Profile via `"profile"`
6. `evaluate_policy` with `instanceId` → should return true (City matches)
7. Create second `Profile` with different City, link to second Customer, evaluate → false
8. If owned-2 is done, repeat with `add_policy` JSON form

## Out of scope

- Atomic create Customer+Profile (S3-B2 W)  
- Dot syntax support

## Definition of Done

- [x] evaluate_policy with store-linked owned instance returns correct true/false  
- [x] Automated tests added (2 new — matching and non-matching cases)  
- [x] 1636 tests green  

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
