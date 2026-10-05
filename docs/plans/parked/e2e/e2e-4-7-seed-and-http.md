# e2e-4-7 — Seed + demo.http honor constraints

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** 4-6  
**Fleet:** P3-7, P3-8  

## Objective

`MakeSampleValue` honors `pattern` / range / required. Seed failure is loud (no silent skip). `demo.http` bodies pass the emitted DTO attributes.

## Exact steps

1. Tests: pattern-constrained root seeds; demo.http body satisfies `[RegularExpression]`/`[Range]`/`[Required]`.  
2. One sample-value function used by seed **and** HttpFileGenerator.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `MinimalApiGenerator.cs` (`MakeSampleValue` / seed) | |
| `src/Poly.DslCompiler/HttpFileGenerator.cs` | |

## Status

**Status:** Not Started  
**Claimed by:**  
