# e2e-r-8 — Date parameter arithmetic

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** r-1  
**Fleet:** P2-4  

## Objective

`assign DueDate to d + 30` where `d: Date` is an action param compiles (AddDays lowering must not key on `PropertyAccess` only).

## Exact steps

1. Failing test: `Export_DateParamPlusDays_Compiles` (or analysis+lower without CS0019).
2. Widen AddDays specialization to param-typed dates (paramEnv type).
3. Not p1 — no `days` token required; existing `+ 30` lowering.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DomainExpressionLoweringPass.cs` (AddDays / date arith arm) | Q3′ throw arms (e2e-2) |
| tests | |

## Status

**Status:** Not Started  
**Claimed by:**  
