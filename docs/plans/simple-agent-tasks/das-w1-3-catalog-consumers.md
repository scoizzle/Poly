# DAS W1.3 — Retarget consumers to the catalog

**Wave:** W1 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) §4.2, §5  
**Difficulty:** Large  
**Status:** `[ ]`  
**Prereq:** W1.2  

## Objective

Lookup surface and product consumers read the catalog (or dual-read with catalog primary). SA fallthrough lives in **one** helper.

## Tasks

- [ ] W1.3.1 Retarget `DomainSemanticLookupExtensions` to catalog.
- [ ] W1.3.2 Runtime: `TryResolveAction` / related paths use catalog helper (sibling-path: analysis-present only until W4).
- [ ] W1.3.3 MCP describe routes use catalog (entity/stage/action/policy/relationship).
- [ ] W1.3.4 Evolution mutation index resolution uses catalog.
- [ ] W1.3.5 Lowering type/relationship resolve prefers catalog when analysis present.
- [ ] W1.3.6 Tests: existing DACR fail-closed + new catalog-backed describe/runtime cases still green.

## Primary files

- `Poly/DomainModeling/Analysis/DomainSemanticLookupExtensions.cs`
- `Poly/DomainModeling/DomainEntityInstance.cs`
- `Poly/DomainModeling/DomainInstanceStore.cs`
- `Poly/DomainModeling/Evolution/*`
- `Poly.Mcp/Tools/OracleTool.cs`
- `Poly/DomainModeling/Lowering/*`

## Acceptance criteria

- [ ] No new consumer of ARM/MTI/DTLM as *separate* sources of truth without going through catalog API.
- [ ] SA implemented once; documented.
- [ ] Build + tests green; sibling-path considered for any dual path left.

## Progress notes

(empty)
