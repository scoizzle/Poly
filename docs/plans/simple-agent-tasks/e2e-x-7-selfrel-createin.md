# e2e-x-7 — Self-relationship `create in`

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** x-6  
**Fleet:** P3-16 · Repro: `selfrel-createin.poly`

## Objective

`IsBackReference` must not mean “self-rel”. Separate self-relationship from back-reference. CS1503 gone.

## Exact steps

1. Failing full-solution compile of `selfrel-createin.poly`.  
2. Fix `EntityStructureAnalyzer` (and any consumer that conflates the two). Exporter + `EffectLoweringPass` must agree.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `EntityStructureAnalyzer.cs` | `MinimalApiGenerator` |
| `DomainToCSharpExporter.cs` / `EffectLoweringPass.cs` (create-in args) | |

## Status

**Status:** Not Started  
**Claimed by:**  
