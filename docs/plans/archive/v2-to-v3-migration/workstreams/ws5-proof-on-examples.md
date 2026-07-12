# Workstream WS5: Proof on Living Specs & Roadblocks

**Phase**: 1  
**Priority**: High (validates the whole approach)  
**Owner**: Grok (orchestrator)  
**Status**: Complete — both proofs pass (1253 tests). Comparison operators and allocation fix delivered.  
**Last Updated**: 2026-06-22 (updated test counts, DomainChange count, resolved gaps noted)

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

**Documented gaps (most resolved during WS1** — see `ws7-v3-expressiveness-audit.md` for refreshed audit):
1. ✅ Cross-entity mutation — `AssignEffect.cs` exists with `Target` + `Value` DomainExpression. RelationshipNav enables cross-entity references.
2. ✅ Dynamic calculation — All arithmetic, DateOperation, RelationshipNavigation, and Comparison operators on DomainExpression.
3. ✅ Conditional effects — `ConditionalEffect.cs` + `InvokeActionEffect.cs` both exist.
4. ✅ Entity inheritance — `Entity.ParentEntityName` + `SetEntityParentChange` + full analyzer support.
5. ✅ InvokeAction parameter binding — `InvokeActionEffect.cs` with PropertyBinding parameter bindings.
Remaining: executable lowering path (Phase 2/WS8).

**Milestone: Incremental Analysis (June 2026)**
- `GetAffectedNodes` replaced by `ModifiedNodes` from `DomainMutationContext` — eliminates 80-line switch over 66 `DomainChange` subtypes and redundant O(t) type scans.
- Wired into `Apply(..., priorAnalysis)` overload so incremental analysis receives real modified-node lists from mutation context.
- **66 concrete `DomainChange` subtypes** (all entity, value type, event, primitive, relationship, stage, action, policy, effect, parameter, constraint, event subscription, contract operations).
- Total test count: **1253 (all passing)**.

**Next targets:**
- WS5 exit criteria are met (PersonLifecycle + ≥1 roadblock via evolution layer). Incremental analysis milestone achieved.
- Handoff to WS4 for trace quality improvements and WS6 (orchestration hygiene).
- Phase 4 can use the documented gaps for scoping.

**Approach (per Core Principles):**
- Build the working proof test first.
- Keep every increment building + test-green.
