# e2e-3-1 — Emit HasOne/HasMany/HasForeignKey

**Difficulty:** M  
**Status:** `[ ]`  
**Needs:** e2e-g0  

## Objective

`OnModelCreating` configures relationships from existing `StorageAnalyzer` / `StorageModel` IR. No parallel mapping types.

## Exact steps

1. Golden: Order/OrderLine (or warehouse) — generated DbContext contains FK + nav config implied by storage metadata.  
2. Owned navs: only if storage already says owned.  
3. Missing storage metadata → fail closed (no empty `HasOne()` stubs).

## File ownership

| Edit | Do not edit |
|------|-------------|
| `src/Poly.DslCompiler/DbContextGenerator.cs` | `MinimalApiGenerator` |
| `StorageAnalyzer.cs` only if a real hole | new mapping types |
| compiler IR smoke tests | |

## Status

**Status:** Not Started  
**Claimed by:**  
