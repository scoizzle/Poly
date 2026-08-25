# e2e-x-2 — `for`-invoke inside `-> EntityType`

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** x-1  
**Fleet:** P3-11 · Repro: `07-export/export-edges.poly`

## Objective

`return result0` / `return Failure(...)` must type as `DomainResult<T>` or the form is analysis-rejected (no CS0029).

## Exact steps

1. Failing full-solution or entities compile test.  
2. Lower to `DomainResult<T>` **or** reject at analysis. Prefer reject if lowering is large.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `EffectLoweringPass.cs` / exporter for-invoke | `MinimalApiGenerator` |
| analysis if reject | |

## Status

**Status:** Not Started  
**Claimed by:**  
