# Micro-Task: Implement Minimal Applicator Skeleton That Interprets the First DomainChange Types

**Parent Workstream**: WS1 (Consolidated) - Evolution Layer Applicator + MVP Operations + NodeId Continuity  
**Difficulty**: Medium (requires understanding builders + immutability)  
**Estimated Context**: < 10k tokens

## Objective

Implement the real body of `ApplyChanges` inside `DomainEvolution.cs` so that it can take a list containing the four `DomainChange` types from the previous micro-task and produce a new (or appropriately modified) immutable `Domain` root using the existing V3 fluent builders internally.

This is the first working code that makes the evolution layer do something real instead of identity.

## Required Reading (Strictly Limited)

- Core Engineering Principles (build working code before abstractions; domain model is the key artifact): `docs/decisions/2026-core-engineering-principles.md`
- Evolution layer design (applicator section + "Layering Decision: Evolution on Top of Builders"): `docs/decisions/2026-05-31-evolution-layer-design.md`
- Current skeleton: `Poly/DomainModeling/Evolution/DomainEvolution.cs` (focus on `ApplyChanges`, `Apply`, and the placeholder logic)
- V3 builders that will be used internally: `Poly/DomainModeling/Builders/DomainBuilder.cs` and `Poly/DomainModeling/Builders/EntityBuilder.cs` (the `Entity(string, Action<EntityBuilder>)` and `Build()` paths)
- The four `DomainChange` types created by the previous micro-task

**Do not read** V2 mutation code, full plans, or unrelated analyzers.

## Exact Steps

1. Read only the files listed above.
2. In `DomainEvolution.cs`, replace the body of the private `ApplyChanges(Domain current, IReadOnlyList<DomainChange> changes)` method.
   - Start with an identity for any unrecognized change (for forward compatibility).
   - For the four supported types, use a `DomainBuilder` (or direct record construction if simpler) seeded from the current domain's state, apply the change, and return the new `Domain`.
   - Keep it brutally simple — one change type at a time, no clever batch merging yet.
3. Wire NodeId preservation for the trivial case: when a change does not affect a subtree, the resulting records should carry forward the original `Node.Id` values where possible (use `with { Id = ... }` on the records that support it, or builder support if it exists).
4. Update the placeholder `GetAffectedNodes` to return something reasonable for the supported changes (the affected entity/property nodes).
5. Ensure `DomainEvolution.Apply(...)` now actually produces different roots on real changes.
6. Extend the tiny test from the previous micro-task (or a new one) to prove:
   - Calling Apply with an AddEntityChange actually adds the entity (new root has it).
   - The original `current` passed in is untouched (immutability).
   - A basic analysis can be run on the result.

## Verification

- [ ] Full solution builds cleanly (`dotnet build`).
- [ ] A test demonstrates that `Apply` with a real supported change produces a new `Domain` containing the change (e.g. the added entity exists and has the expected name).
- [ ] The original domain reference remains unchanged.
- [ ] No unnecessary abstractions, visitor patterns, or "pluggable applicator" machinery. Direct, working code.
- [ ] NodeId preservation attempt is visible in the code for at least the unchanged parts.
- [ ] Follows the "evolution on top of builders" layering decision.

## Output

- Modified `Poly/DomainModeling/Evolution/DomainEvolution.cs` with a real (if minimal) `ApplyChanges` implementation for the first four change types.
- Updated or new test that exercises the end-to-end path from `new DomainEvolution(d).Apply(changes)` through to a modified root.
- The evolution layer is now capable of making *some* changes instead of being a pure no-op.

## Status

**Claimed by**:  
**Started**:  
**Status**: Not Started / In Progress / Done (summary submitted)

**Notes / Blockers**:

---

**After completing this task**: Create a summary file in `../agent-summaries/` using `TEMPLATE-task-summary.md`. Only update the Status line here. Do not edit the master roadmap or workstream files yourself.