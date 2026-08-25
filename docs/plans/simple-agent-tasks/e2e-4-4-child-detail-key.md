# e2e-4-4 — Child detail filters by `{id}`

**Difficulty:** S  
**Status:** `[x]` 2026-08-13 — detail filters by disambiguated child key  
**Prereq:** 4-3  
**Fleet:** P3-4  

## Objective

Shadow-keyed child detail must not `FirstOrDefault()` ignoring `{id}`.

## Exact steps

1. Test: two children, `{id}` returns the matching one (emit assert or compile+logic).  
2. Filter by child key.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `MinimalApiGenerator.cs` (detail GET) | |

## Status

**Status:** Not Started  
**Claimed by:**  
