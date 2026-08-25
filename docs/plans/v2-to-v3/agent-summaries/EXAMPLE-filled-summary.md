# Task Summary

**Task ID**: ws3-add-basic-property-operation  
**Agent ID**: @small-claude-3  
**Date Completed**: 2026-06-03  
**Parent Workstream**: WS3 - MVP Operation Support  
**Status**: Done

## What Was Attempted
Implement the ability to add a new `Property` to an existing `Entity` through the evolution layer using V3 immutable builders.

## What Was Actually Done
- Created a simple `AddPropertyChange` record in `Poly/DomainModeling/Evolution/Changes/AddPropertyChange.cs`
- Added handling logic in the main `EvolutionApplicator.cs` (new file under the Evolution folder)
- The applicator finds the target Entity, uses `EntityBuilder` to produce an updated version with the new property, then rebuilds the top-level `Domain`
- The change is recorded as a step in `EvolutionTrace`
- Added a basic unit test in `Poly.Tests/Evolution/ApplicatorTests.cs` (new test method `AddProperty_SuccessfulEvolution`)

## Verification Performed
- [x] Build succeeds (dotnet build on Poly and Poly.Tests)
- [x] The new unit test passes
- [x] New property appears correctly on the resulting Entity after evolution
- [x] Original input Domain is not mutated (verified with reference check + deep equality)
- [x] Trace contains the expected step for the property addition
- [x] Followed Core Engineering Principles (especially "build working code before abstraction")

## Impact on the Overall Plan
- Completes one of the core MVP operations needed for WS3.
- Unblocks parts of WS5 (proof on examples) that need property modification.
- No changes needed to the master roadmap at this time.
- This pattern can be reused for other simple "add member" operations.

## New Information / Surprises
- Using the V3 builders inside the applicator was cleaner than I expected.
- Needed to add a small helper method on `EntityBuilder` for "add property to existing builder state" to keep things immutable-friendly.
- Trace step naming should probably be standardized (I used a simple string for now).

## Decision Impact
- This work surfaced the need for a small decision on "standard change description format in traces".
- I created a stub decision record at `docs/decisions/2026-evolution-trace-step-naming.md` (very early draft).

## Blockers / Open Questions
- None for this specific task.
- Open question for future similar tasks: Should we always go through builders, or have some direct record-with helpers for performance in hot paths?

## Files Changed (for orchestrator review)
```diff
+ Poly/DomainModeling/Evolution/Changes/AddPropertyChange.cs
+ Poly/DomainModeling/Evolution/EvolutionApplicator.cs   (added handling)
+ Poly.Tests/Evolution/ApplicatorTests.cs               (new test)
~ Poly/DomainModeling/Builders/EntityBuilder.cs         (small helper added)
```

## Notes for the Orchestrator
The helper I added to EntityBuilder feels like it might belong in a more general "immutable update helpers" area later. Worth discussing in the next sync.

---

**Agent Signature**: @small-claude-3  
**Time spent on this task**: ~28 minutes

---

**Orchestrator Review Section** (to be filled by orchestrator)

- [ ] Work reviewed and accepted
- [ ] Integrated into master roadmap / WS3
- [ ] Decision stub reviewed / promoted
- Notes: 
