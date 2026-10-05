# e2e-x-3 — Unique create unwrap locals

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** x-2  
**Fleet:** P3-12 · Repro: `isolated-two-creates.poly`

## Objective

Two `create` of the same type must not emit `{camel}Result` twice (CS0128). Per-statement sequence in unwrap-local naming.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DomainToCSharpExporter.cs` / `EffectLoweringPass.cs` (local names) | |

## Status

**Status:** Not Started  
**Claimed by:**  
