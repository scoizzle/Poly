# e2e-4-6 — Grandchildren fail loud or nest

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** 4-5  
**Fleet:** P3-6 · Repro: warehouse Delivery  

## Objective

Non-root grandchildren must not emit root-scoped actions. **Prefer fail-loud at analysis** (smaller). Nesting the aggregate chain is allowed if you already have the parent ctx.

## Exact steps

1. Test: `warehouse.poly` Delivery actions are either nested under the chain or analysis-rejected.  
2. No floating root endpoints.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `MinimalApiGenerator.cs` | new transport IR |
| analysis only if you fail-loud there | |

## Status

**Status:** Not Started  
**Claimed by:**  
