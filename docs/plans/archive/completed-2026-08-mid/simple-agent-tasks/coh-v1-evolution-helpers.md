# COH-V1 — Evolution mutation helpers

**Stream:** V  
**Difficulty:** M  
**Status:** `[x]` — DONE 2026-08-06  
**Prereq:** COH-0

## Implementation notes

- `DomainMutationContext` already routed all `Update*` wrappers through
  `ReplaceInList<T>` (context-level dedup existed). The residual duplication was
  the **append-child shapes** in `DomainChange.ApplyTo` (11 near-identical
  `Xs = e.Xs.Append(item).ToList()` sites).
- Added three generic append helpers to `DomainMutationContext`:
  `AppendChildToEntity`, `AppendChildToStage`, `AppendChildToAction` (getter +
  rebuilder pair; delegate to `UpdateEntity`/`UpdateStage`/`UpdateAction` so
  fail-loud zero-match via `RequireUpdate` is preserved).
- Routed 11 `ApplyTo` sites through the helpers: AddPropertyToEntity,
  AddStage, AddAction, AddPolicyToEntity, AddPolicyToStage, AddEffectToAction,
  AddParameterToAction, AddOnEntryEffectToStage, AddOnExitEffectToStage,
  AddStageSubscription, AddEntitySubscription.
- `Remove*` shapes already used `RemoveFromEntity` — untouched. No public fluent
  API break (helpers additive; `Update*` wrappers still exist).
- Verified: build 0 errors, 1855/1855 tests green (evolution/apply + fail-loud
  zero-match tests cover all routed sites).  

## Objective

Deduplicate near-identical list replace patterns in `DomainMutationContext` / `DomainChange.ApplyTo` via shared helpers (e.g. `ReplaceInList`, shared entity nested update shapes). No new evolution framework.

## Required reading

- abstraction-gaps Finding 2  
- `DomainMutationContext.cs`, `DomainChange.cs`  

## Exact steps

1. Identify 3+ near-identical Update* patterns.  
2. Extract private/shared helpers.  
3. Route existing methods through helpers.  
4. Evolution/apply tests green; fail-loud zero-match preserved.

## Verification

- [ ] Evolution tests green  
- [ ] No API break for public fluent surface (or intentional + tests)  

## File ownership

- **Edit:** Evolution/* mutation types only  
- **Do not edit:** Analysis, Runtime, Parsing  

## Status

**Status:** Not Started  
