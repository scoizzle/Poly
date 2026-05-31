# Micro-Task: Implement Add Property Operation (Simple)

**Parent Workstream**: WS3 - MVP Operation Support  
**Difficulty**: Small Model Friendly  
**Estimated Context**: < 5k tokens  
**Target Model Size**: Small / Medium

## Objective
Implement the ability to add a new `Property` to an existing `Entity` through the evolution layer, using the V3 immutable builders.

## Context You Must Read First (do not skip)

1. Core Engineering Principles (especially "build working code before abstraction" and "domain model is the key artifact")  
   → `docs/decisions/2026-core-engineering-principles.md`

2. The relevant section in AGENTS.md about Property and Entity handling.

3. How V3 builders work for properties (look at `EntityBuilder.Property` and the final `Build()` method in `Poly/DomainModeling/Builders/EntityBuilder.cs`).

4. The current `Property` record definition (`Poly/DomainModeling/Property.cs`).

5. The evolution layer sketch in `docs/decisions/2026-05-31-evolution-layer-design.md` (just the "Target Shape" section).

**Do not read the entire port plan or all workstreams** unless explicitly needed.

## Exact Steps

1. Create a new simple `DomainChange` subtype (or use the emerging adapter) called something like `AddPropertyChange` that carries:
   - The target Entity (by name or reference)
   - The new Property definition

2. In the evolution applicator (the code that turns changes into a new immutable Domain), handle this change by:
   - Finding the target Entity in the current immutable root
   - Using the V3 builder pattern (or direct record construction if simpler) to produce a new version of that Entity with the additional Property
   - Producing a new top-level `Domain` containing the updated Entity

3. Make sure the operation is recorded in the generated `EvolutionTrace`.

4. Write a small unit test that:
   - Starts with a simple Domain containing one Entity
   - Applies an "add property" change via the evolution layer
   - Verifies the new Property exists on the resulting Entity
   - Verifies the original root was not mutated (immutability check)

## Verification Checklist

- [ ] The code compiles cleanly
- [ ] The unit test passes
- [ ] The new property appears in the resulting Domain after evolution
- [ ] No mutation of the input Domain (use reference equality or deep equality check)
- [ ] Trace contains a step for the property addition

## Output Expected

- One new file or addition in the evolution layer code for the operation.
- One new test file or test method.
- Update to the parent workstream file (`docs/plans/v2-to-v3/workstreams/ws3-mvp-operations.md`) marking this item complete.

## Status

**Claimed by**: (fill in when you start)  
**Status**: Not Started / In Progress / Done (summary submitted)

---

**Reminder**: After finishing, create a task summary in `../agent-summaries/` using the template. Do **not** edit the master roadmap or workstream files yourself. Follow the Core Principles. Prefer the simplest correct implementation.