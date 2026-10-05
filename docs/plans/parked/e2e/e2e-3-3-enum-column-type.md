# e2e-3-3 — Enum HasColumnType is provider-valid

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** 3-2  
**Fleet:** P7-5 · Repro: `13-packs/warehouse.poly` `.HasColumnType("Sku")`

## Objective

`EnsureCreated` must not die on enum store type. Use INTEGER/int or drop `HasColumnType` for enums.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DbContextGenerator.cs` / pack column mapper | |

## Status

**Status:** Not Started  
**Claimed by:**  
