# e2e-0-2 — Execution-model + capability inventory

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** none (parallel with 0-1; different files)  

## Objective

Historical inventories no longer list deleted Effect IR or an Event DomainType.

## Exact steps

1. Edit `docs/interpretation/domain-execution-model.md`: remove `DeleteEntityInstance`, `LinkRelationshipEffect`, `UnlinkRelationshipEffect`, `TransitionRelationship` as current runtime/IR. Linking existing instances = `DomainInstanceStore.Link` / MCP `link_instances` only.
2. Same pass on `docs/domainmodeling-capability-inventory.md`.
3. DateOperation: authoring = p1; VM AddDays is not a gap. ParameterAccess: product spelling is PropertyAccess + paramEnv (point at parent L3).
4. Do not rewrite those docs into new roadmaps.

## Verification

- [ ] Grep those two files: zero live claims of the four deleted effect types as implemented IR  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `docs/interpretation/domain-execution-model.md` | `poly-dsl-guide.md` |
| `docs/domainmodeling-capability-inventory.md` | `Poly/**` |

## Status

**Status:** Done  
**Claimed by:** opencode (fleet agent, e2e-0) — 2026-08-13  
**Verified:** no live claims of the four deleted effect types; DateOperation authoring → p1; ParameterAccess → L3 wording
