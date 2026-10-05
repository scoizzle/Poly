# e2e-s-3 — Export subscription order + `all` union

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** s-2 · **wave 4** (after e2e-1-2)  
**Fleet:** P6-2 export, P6-3  

## Objective

Export handler order is **stage-scoped then entity-level** (runtime + guide §7). Export `all` uses the same StageNames union as s-2.

## Exact steps

1. Test: stage write then entity write leaves the **stage** value (export-generated or order-assert on emit). Name: `Export_SubscriptionHandlers_StageThenEntity`.
2. Test: spread-set `all` in exported C# matches runtime (or emit asserts union).
3. Touch only subscription-registration / handler emit in the exporter.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DomainToCSharpExporter.cs` (subscription emit only) | Create unique, Q3′ policies, action guards |
| tests | `MinimalApiGenerator` |

## Status

**Status:** Not Started  
**Claimed by:**  
