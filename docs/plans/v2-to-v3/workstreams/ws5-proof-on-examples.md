# Workstream WS5: Proof on Living Specs & Roadblocks

**Phase**: 1  
**Priority**: High (validates the whole approach)  
**Owner**: TBD  
**Status**: In Progress (orchestrator-led kickoff after WS4 full-send simplification)  
**Last Updated**: 2026-06 (WS5 kickoff: PersonLifecycle proof via evolution layer)

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
- WS1 foundation + basic traces complete.
- WS7 expressiveness audit has mapped the chosen scenarios (PersonLifecycle + roadblocks) against current V3 capabilities.

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

## Current Status (May 2026 — Both Proofs Complete)

**PersonLifecycle proof**: `PersonLifecycle_DocumentedShape_ProvenViaEvolutionLayer` — passes. The documented shape (complex DomainExpression policies, Exists/NotExists+Owned guards, OnEntry Publish bindings with Subtract, events, ValueTypes, stage actions, Create+Transition effects) works end-to-end via `DomainEvolution.Evolve()`.

**Library domain proof**: `LibraryDomain_LoanLifecycle_ProvenViaEvolutionLayer` — passes (1025/1025 total). Core Library domain (Book, Loan, Member, Fine entities with stages, events, relationships, actions with CreateEntityInstance+initializers, StageTransition, PublishEvent with bindings) built entirely through evolution layer.

**Blockers encountered and fixed (PersonLifecycle path):**
- Action-targeting change types (`AddParameterToActionChange`, etc.) only searched entity-level actions, not stage-level actions. All 7 types updated to fall back to stage-level search.
- Test assertion was checking step3 diagnostics for step2's changes (fixed).

**Known V3 gaps documented as Phase 4 input (from Library proof):**
1. Cross-entity mutation — CheckoutBook/ReturnBook need to modify Book.AvailableCopies
2. Dynamic calculation — RenewLoan needs arithmetic (increment, date extension)
3. Conditional effects — ReportLost needs ConditionalEffect + InvokeAction
4. Entity inheritance — No ParentEntity in V3 Entity
5. InvokeAction parameter binding — FulfillReservation→CheckoutBook not supported

**Milestone: Incremental Analysis (June 2026)**
- `GetAffectedNodes` in `DomainEvolution.cs` now returns real affected nodes for all 40+ `DomainChange` subtypes.
- Wired into `Apply(..., priorAnalysis)` overload so incremental analysis receives real affected-node lists.
- 5 new `DomainChange` subtypes added covering relationship properties, property constraints, and domain rename.
- Total test count: 1031 (all passing).

**Next targets:**
- WS5 exit criteria are met (PersonLifecycle + ≥1 roadblock via evolution layer). Incremental analysis milestone achieved.
- Handoff to WS4 for trace quality improvements and WS6 (orchestration hygiene).
- Phase 4 can use the documented gaps for scoping.

**Approach (per Core Principles):**
- Build the working proof test first.
- Keep every increment building + test-green.
