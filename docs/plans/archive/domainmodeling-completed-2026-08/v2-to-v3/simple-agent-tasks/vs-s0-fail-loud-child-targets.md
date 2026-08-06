# Micro-Task: Fail-loud when stage/property (child) target is missing

**Suite:** [`vs-README.md`](vs-README.md) **#0.1b**  
**Parent:** Slice 0 (residual of **#0.1**)  
**Depends on:** **#0.1** Done (prefer **#0.1a** first so tests can assert FailureSummary)  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~5k tokens  
**Status:** [x] **Done** (2026-07-12) — `UpdateStage` / `UpdateProperty` / `UpdateRelationshipStage` return false when child name missing; tests for missing stage action/policy/on-entry and missing property type/constraint; success paths retained.  

## Objective

`UpdateStage` / `UpdateProperty` (and similar child updaters) must return **false** when the parent entity/relationship exists but the **named stage or property does not** — so `RequireUpdate` records an error instead of a silent identity transform.

## Background

Today if entity `Order` exists but stage `Shipped` does not, `UpdateStage("Order", "Shipped", …)` still returns `true` after rewriting stages unchanged. Same pattern for `UpdateProperty` and likely `UpdateRelationshipStage`.

## Required Reading

- `Poly/DomainModeling/Evolution/DomainMutationContext.cs` — `UpdateStage`, `UpdateProperty`, `UpdateRelationshipStage`
- Call sites in `DomainChange.cs` that use those helpers with `RequireUpdate`
- Evolution tests under `Poly.Tests/DomainModeling/Evolution/`

## Exact Steps

1. Change `UpdateStage` to return `false` when entity is found but **no** stage name matches (do not mark ModifiedNodes / do not replace entity if nothing changed — or only update when a stage was actually transformed).
2. Same for `UpdateProperty` (entity found, property name missing → `false`).
3. Same for `UpdateRelationshipStage` (relationship found, stage name missing → `false`).
4. Tests:
   - Add action/policy/effect to **missing stage** on an **existing** entity → `Succeeded == false`, rolled back.
   - Update/remove **missing property** on existing entity → fail loud.
5. Keep entity-missing paths green from #0.1.

## Verification

- [ ] Missing child name fails loud; missing entity still fails loud
- [ ] Successful real stage/property updates still work
- [ ] Build + evolution tests green

## Output

- `DomainMutationContext.cs` (+ tests)
- Summary: `../agent-summaries/vs-s0-fail-loud-child-targets-summary.md`

## Out of Scope

- Diagnostics inject (#0.1a) unless already Done — still assert `Succeeded`/`WasRolledBack`
- Event subscription / contract ApplyTo list (#0.1c)

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
