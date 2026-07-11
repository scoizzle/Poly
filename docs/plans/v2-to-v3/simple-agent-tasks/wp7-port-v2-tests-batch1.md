# Micro-Task: Port or delete first V2 test batch

**Parent**: WP7  
**Difficulty**: Medium–Hard  
**Estimated Tokens**: ~12k  
**Status**: [ ] Not Started  
**Depends on**: `wp7-inventory-v2-tests-and-demos.md` (use its first batch list)

## Objective

**Aggressively** port or delete the **first batch** of V2-only tests from the inventory. Shrink `Poly.Tests` V2 surface.

## Exact Steps

1. Open `docs/plans/v2-to-v3/spikes/v2-port-inventory.md` — take **Batch 1** only.
2. For each file/class in the batch:
   - If redundant with V3 suites → **delete** (or empty and remove from project if needed).
   - If valuable → rewrite against `DomainFactory` / `DomainEvolution` / `DomainQueries` / V3 types only.
3. Ensure no new `using Poly.Data.Modeling` in ported files.
4. Run affected TUnit tests; fix until green.
5. Update inventory: mark batch 1 complete.

## Verification

- [ ] Batch 1 files gone or fully V3
- [ ] `dotnet build` + targeted tests green
- [ ] Grep: batch files have zero `Poly.Data.Modeling`

## Out of Scope

- Full V2 tree deletion (WP8)
- Demos (next batch task)
- Actor/features not on V3 — leave as Blocked in inventory
