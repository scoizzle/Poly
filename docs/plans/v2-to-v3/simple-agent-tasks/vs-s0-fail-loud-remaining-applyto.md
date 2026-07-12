# Micro-Task: RequireUpdate on remaining ApplyTo paths

**Suite:** [`vs-README.md`](vs-README.md) **#0.1c**  
**Parent:** Slice 0 (residual of **#0.1**)  
**Depends on:** **#0.1** Done  
**Difficulty:** Small–Medium  
**Estimated Context:** ~6k tokens  
**Status:** [x] **Done** (2026-07-12) — audit: every `ApplyTo` that calls `Update*` uses `RequireUpdate` (0 remaining).  

## Objective

Every `DomainChange.ApplyTo` that calls `UpdateEntity` / `UpdateType` / `UpdateRelationship` / `UpdateImportedContract` / `UpdateContractBinding` (or equivalent “must find target”) must use `RequireUpdate` so missing targets never silent-succeed.

## Background

~33 change types already use `RequireUpdate`. A remaining set (~16) still call `Update*` without it, including:

- `AddConstraintToDomainTypeChange` / `RemoveConstraintFromDomainTypeChange`
- `AddPropertyToEventChange` / `RemovePropertyFromEventChange`
- `SetPrimitiveTypeCategoryChange`
- `AddEventSubscriptionChange` / `RemoveEventSubscriptionChange` / correlation / routing / event-parameter setters
- `SetEntityParentChange`
- `AddContractEndpointChange` / `RemoveContractEndpointChange` / field-map add/remove

(Re-grep when starting — list may have shifted.)

## Required Reading

- `Poly/DomainModeling/Evolution/DomainChange.cs` — search `ApplyTo` methods **without** `RequireUpdate`
- `DomainMutationContext.RequireUpdate`
- Pattern from an existing change that already uses `RequireUpdate` (e.g. `AddPropertyToEntityChange`)

## Exact Steps

1. Grep/list every `ApplyTo` that calls an `Update*` / finder and lacks `RequireUpdate`.
2. For each: wrap with `RequireUpdate(..., $"… not found — cannot …")` with a clear message.
3. For updates that transform a nested collection by name (subscription event type, etc.): if the parent updates successfully but the **nested** name was not found, either:
   - make the Update helper return false when nothing nested matched, **or**
   - check before/after in ApplyTo and `Errors.Add` — prefer false from helper for consistency with #0.1b.
4. Add **at least 3** focused tests covering different families (e.g. missing entity for event subscription; missing type for constraint; missing contract for endpoint) — not necessarily one per change type.
5. Do not change successful happy-path semantics for existing applicator tests.

## Verification

- [ ] No `UpdateEntity`/`UpdateType`/`UpdateRelationship`/`UpdateImportedContract`/`UpdateContractBinding` in ApplyTo without `RequireUpdate` (except pure append-to-domain-root adds that do not need a target)
- [ ] New missing-target tests fail loud
- [ ] Existing evolution applicator suite green
- [ ] Build green

## Output

- `DomainChange.cs` (+ helpers if needed) + tests
- Summary: `../agent-summaries/vs-s0-fail-loud-remaining-applyto-summary.md`

## Out of Scope

- MCP tools
- Redesigning evolution API
- Effect execution

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
