# Micro-Task: Implement Add Stage Operation (Simple)

**Parent Workstream**: WS3 - MVP Operation Support  
**Difficulty**: Small Model Friendly  
**Estimated Context**: < 5k tokens

## Objective
Implement adding a new `Stage` to an `Entity` through the evolution layer.

## Context You Must Read First

- Core Engineering Principles.
- V3 `Stage` record and `StageBuilder`.
- How stages are attached in `EntityBuilder` and the final `Build()`.

## Exact Steps

1. Create a simple `AddStageChange` (or use the adapter).
2. Handle it in the applicator by finding the Entity and producing an updated version with the new Stage (using builders where possible).
3. Record the step in the trace.
4. Write a small test: start with Entity without stages → add one stage → verify it exists on the result.

## Verification

- [ ] Builds
- [ ] Test passes
- [ ] Original not mutated
- [ ] Trace updated

## Output

- Change handling code
- One test
- Update parent workstream

## Status

**Claimed by**:  
**Status**: Not Started / In Progress / Done (summary submitted)