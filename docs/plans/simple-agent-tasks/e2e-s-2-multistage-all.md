# e2e-s-2 — Multi-stage `all` set predicate

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** s-1  
**Fleet:** P6-2  

## Objective

`when all orders Ready, Delivered` fires when the set is **spread** across those stages. Today runtime + export require the same single stage.

## Exact steps

1. Failing runtime test: two orders, one Ready and one Delivered → `all` fires. Name: `Subscription_All_SpreadStages_Fires`.
2. Evaluate the set predicate against the **union** of declared `StageNames` on the **runtime** path only. Export path is s-3.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DomainInstanceStore.cs` / subscription match helpers | `DomainToCSharpExporter` (s-3) |
| tests | |

## Status

**Status:** Not Started  
**Claimed by:**  
