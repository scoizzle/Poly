# Micro-Task: Fail-loud when remove-by-name finds zero matches

**Suite:** [`vs-README.md`](vs-README.md) **#0.1d** *(optional — does not block Slice 1)*  
**Parent:** Slice 0 residual  
**Depends on:** **#0.1** Done  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~5k tokens  
**Status:** [ ] Not Started  

## Objective

Removing a property/stage/action/policy **by name** must fail loud when the **parent exists** but **no child with that name** exists (today many removes use `Where` filters and succeed with zero effect).

## Background

`UpdateStage` / `UpdateProperty` now fail when the child is missing for **transform** paths. Removals like `RemovePropertyFromEntityChange` still call `UpdateEntity` + `Where(...)` — entity found → success even if the property name never matched.

Same pattern may apply to: remove stage, remove action, remove policy from entity/stage/action, remove parameter, etc.

## Required Reading

- `Poly/DomainModeling/Evolution/DomainChange.cs` — Remove* changes that use `Where` / filter
- `DomainMutationContext.RequireUpdate`
- Existing fail-loud tests in `EvolutionRollbackTests.cs`

## Exact Steps

1. Inventory Remove* ApplyTo that succeed whenever parent exists.
2. For each (or a representative set): if no element was removed, `Errors.Add` / return false path.
   - Prefer helper: e.g. count before/after, or `TryRemove*` returning bool.
3. Tests: at least remove missing property on existing entity; remove missing stage; remove missing action — all `Succeeded == false`.
4. Happy-path remove still works when name exists.

## Verification

- [ ] Zero-match remove fails loud
- [ ] Real remove still succeeds
- [ ] Build + evolution tests green

## Output

- DomainChange / DomainMutationContext + tests
- Summary under `../agent-summaries/`

## Out of Scope

- MCP tools
- 0.2 stage-action assign semantics

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
