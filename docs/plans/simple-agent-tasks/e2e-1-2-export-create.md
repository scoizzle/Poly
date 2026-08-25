# e2e-1-2 — Exported Create checks unique

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** 1-1  
**Wave-4 note:** this is the **first** `DomainToCSharpExporter` edit. Do not start if another wave-4 task is in progress.

## Objective

Stop skipping unique in Create factories (`DomainToCSharpExporter` ~1052). Generated `Create` rejects duplicates the same way as other constraints.

## Exact steps

1. Failing export test: unique property appears in Create validation; compiled Create fails on collision **or** the emitted source contains the check (compile-oracle preferred).
2. Delete the skip. Reuse the same constraint-emit path as `required`/`range`.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DomainToCSharpExporter.cs` (Create / constraint emit only) | Q3′ policy methods, subscriptions, Minimal API |
| tests | `DbContextGenerator` |

## Status

**Status:** Not Started  
**Claimed by:**  
