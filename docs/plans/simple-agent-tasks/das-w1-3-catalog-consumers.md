# DAS W1.3 — Retarget consumers to the catalog

**Wave:** W1 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) §4.2, §5  
**Difficulty:** Large  
**Status:** `[x]`  
**Prereq:** W1.2  

## Objective

Lookup surface and product consumers read the catalog (or dual-read with catalog primary). SA fallthrough lives in **one** helper.

## Tasks

- [x] W1.3.1 Retarget `DomainSemanticLookupExtensions` to catalog.
- [x] W1.3.2 Runtime: `TryResolveAction` / related paths use catalog helper (sibling-path: analysis-present only until W4).
- [x] W1.3.3 MCP describe routes use catalog (entity/stage/action/policy/relationship).
- [x] W1.3.4 Evolution mutation index resolution uses catalog.
- [x] W1.3.5 Lowering type/relationship resolve prefers catalog when analysis present.
- [x] W1.3.6 Tests: existing DACR fail-closed + new catalog-backed describe/runtime cases still green.

## Primary files

- `Poly/DomainModeling/Analysis/DomainSemanticLookupExtensions.cs`
- `Poly/DomainModeling/DomainEntityInstance.cs`
- `Poly/DomainModeling/DomainInstanceStore.cs`
- `Poly/DomainModeling/Evolution/*`
- `Poly.Mcp/Tools/OracleTool.cs`
- `Poly/DomainModeling/Lowering/*`

## Acceptance criteria

- [x] No new consumer of ARM/MTI/DTLM as *separate* sources of truth without going through catalog API.
- [x] SA implemented once; documented.
- [x] Build + tests green; sibling-path considered for any dual path left.

## Progress notes

- Catalog helpers: `GetCatalog`, `GetActionResolution`, `GetMutationIndex`, `GetTypeLookup`, `GetRelationshipLookup` (catalog primary, dual-read bags).
- SA only in `TryResolveAction` (doc + design note).
- Runtime/MCP/evolution/lowering retargeted; fail-closed tests strip catalog+bag (sibling-path); new catalog-backed cases when only raw bag stripped.
- `DomainCatalogPass` no longer skips on `HasStructuralFailure` while dual-write is on (composes whenever source bags exist).
- Build green; suite filter green (1739 passed).
