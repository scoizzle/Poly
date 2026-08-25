# e2e-3-4 — SqlServer key/unique text is bounded

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** 3-3  
**Fleet:** P7-6 · Repro: `13-packs/booking.poly` `nvarchar(max)` key

## Objective

Key/unique text columns honor `length` (or a documented default bound). Invalid SQL Server key columns gone.

## File ownership

| Edit | Do not edit |
|------|-------------|
| pack SqlServer column mapping + `DbContextGenerator` | |

## Status

**Status:** Not Started  
**Claimed by:**  
