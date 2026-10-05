# e2e-3-5 — Quoted CHECK SQL

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** 3-4  
**Fleet:** P7-7  

## Objective

Column names in CHECK SQL are provider-quoted. `column("order")` works. A `--` in a name cannot comment-out the CHECK. Fail closed on illegal names if quoting is not enough.

## Exact steps

1. Tests: reserved word column; comment-injection name.  
2. Quote + dedupe constraint names. Do not interpolate raw identifiers.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DbContextGenerator.cs` CHECK emit | P5 envelope math (fleet-eval) |
| tests | |

## Status

**Status:** Not Started  
**Claimed by:**  
