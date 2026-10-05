# e2e-3-2 — Secondary unique indexes

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** 3-1  

## Objective

Non-PK `StorageColumn.IsUnique` → EF unique index. Drive from storage metadata only (e2e-1 already sets/uses `IsUnique`).

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DbContextGenerator.cs` (column/index emit) | `DomainToCSharpExporter` Create |
| tests | |

## Status

**Status:** Not Started  
**Claimed by:**  
