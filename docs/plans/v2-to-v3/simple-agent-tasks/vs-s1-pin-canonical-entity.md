# Micro-Task: Pin canonical vertical-slice entity

**Suite:** [`vs-README.md`](vs-README.md) **#1.2**  
**Parent:** Slice 1  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~3k tokens  
**Status:** [ ] Not Started  

## Objective

Choose **one** primary entity name for Slice 2–3 demos/tests: **Person** (Age, lifecycle) **or** **Order**. Document it so later agents stop inventing third names.

## Required Reading

- `Poly/DomainModeling/Examples/PersonLifecycleExample.cs` (or ViaBuilders)
- `Poly/DomainModeling/Examples/Demos/` (Library / ECommerce if present)
- Existing policy tests for entity names used

## Exact Steps

1. Prefer **Person** if Age/policy tests already use it; else **Order** if e-commerce is the stronger path.
2. Write a short decision into:
   - `docs/plans/v2-to-v3/vertical-slice-finish-plan.md` (Slice 1 status / pin note), **or**
   - `docs/plans/v2-to-v3/simple-agent-tasks/vs-README.md` under “Canonical entity”
3. List which test files already use that name (for Slice 2.5).
4. Do **not** rewrite demos unless names conflict badly.

## Verification

- [ ] One name documented as canonical
- [ ] Rationale one sentence in summary
- [ ] No new domain features

## Output

- Doc pin only (+ summary)

## Out of Scope

- Implementing policies
- Deleting the other demo domain

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
