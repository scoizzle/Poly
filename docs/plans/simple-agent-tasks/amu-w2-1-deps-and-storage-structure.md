# AMU-W2.1 — Dependencies honesty + Storage prefers EntityStructure

**Wave:** 2  
**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** W1 product ACs  

## Objective

1. Declare real `Dependencies` for passes that consume topology / entity structure / catalog (no silent order reliance).  
2. `StorageAnalyzer` / StoragePass path prefer `EntityStructureMetadata` for unique key / soft-delete / stage-related facts when present instead of re-deriving solely from property scans.

## Required reading

- Pass `Dependencies` arrays under `Analysis/`  
- `StorageAnalyzer.cs`, `StoragePass.cs`, `EntityStructureMetadata.cs`  
- OwnershipAggregate / topology consumers  

## Exact steps

1. Audit empty `Dependencies => []` publishers and their consumers.  
2. Add deps where missing (e.g. consumers of EffectTopology, EntityStructure).  
3. Storage: if `EntityStructureMetadata` present, use it for overlapping fields; scan only for storage-specific residuals.  
4. Fail closed if StoragePass requires hierarchy and bags missing (align TransportPass posture if not already).  
5. Tests for storage mapping under full domain analyze.

## Verification

- [ ] Pipeline order stable; no circular deps  
- [ ] Storage goldens / DAU-style storage tests green  
- [ ] Build green  

## File ownership

- **Edit:** Storage* + Dependency lines on affected analyzers; storage tests  
- **Do not edit:** MCP, EffectAnalyzer (unless deps line only)  

## Status

**Status:** Not Started  
