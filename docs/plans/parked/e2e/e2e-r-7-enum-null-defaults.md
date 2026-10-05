# e2e-r-7 — Enum defaults, invoke enum args, null

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** r-5  
**Fleet:** P2-8, P2-9, P2-10  

## Objective

- `default(Bogus)` on enum property → analysis reject.  
- Invoke arg `status: "Active"` membership-checked and qualified in lowering; `"Bogus"` rejected.  
- `assign Qty to null` rejected for Number.

## Exact steps

1. Three failing tests (analysis + one export compile for valid enum arg).
2. `CheckDefault` membership when target is enum. Invoke-arg path: same membership + qualify enum literals. `Null` category only for reference/nullable targets.

## File ownership

| Edit | Do not edit |
|------|-------------|
| default/assign/invoke-arg analysis + invoke-arg lowering qualify | `MinimalApiGenerator` |
| tests | |

## Status

**Status:** Not Started  
**Claimed by:**  
