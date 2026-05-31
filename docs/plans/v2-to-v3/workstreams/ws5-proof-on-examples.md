# Workstream WS5: Proof on Living Specs & Roadblocks

**Phase**: 1  
**Priority**: High (validates the whole approach)  
**Owner**: TBD  
**Status**: Not Started  
**Last Updated**: 2026-06-01

## Goal
Demonstrate that the new immutable core + evolution layer is not only theoretically better, but actually solves real problems the current demos and roadblocks have.

## Primary Targets

1. **PersonLifecycle examples** (both the builder version and the manual version)
   - Must construct and evolve cleanly through the new layer.
   - Must exercise `DomainExpression` for guards, initializers, calculations, etc.

2. **At least one documented roadblock scenario**
   - Recommended first target: Library `RenewLoan` (dynamic calculation via `DomainExpression` + potential cross-entity effect via explicit action).
   - Alternative strong candidates: Library `CheckoutBook`/`ReturnBook`, or Healthcare ownership scenarios.

## Entry Criteria
- WS3 (MVP operations) has delivered the operations needed for the chosen scenarios.
- WS4 has usable traces.

## Key Tasks

- [ ] Select the specific roadblock scenario(s) to prove in Phase 1.
- [ ] Implement the scenario(s) using V3 builders + the evolution layer.
- [ ] Verify that analysis catches problems and rollback works as expected.
- [ ] Compare readability / maintainability vs. the old V2 mutation style (qualitative).
- [ ] Update the relevant roadblock .md files with the new solution (or mark as resolved).
- [ ] Produce clear before/after examples for documentation.

## Exit Criteria
- PersonLifecycle examples work end-to-end via the evolution layer.
- At least one roadblock is implemented cleanly without the previous workarounds.
- Evidence (code + traces + before/after notes) that the new approach is superior for the chosen scenarios.
- Any new patterns discovered are captured in decision records or AGENTS.md updates.

## Dependencies
- WS3 (operations)
- WS4 (traces & quality)
- Partial on WS1/WS2 for stability

## Parallelism Notes
This workstream can start early design/prototyping in parallel with WS3, but full validation requires the operations to exist.

## Value
This is one of the most important validation points for the entire port. Strong results here will build confidence for later phases.