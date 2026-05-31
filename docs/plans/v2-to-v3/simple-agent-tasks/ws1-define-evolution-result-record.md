# Micro-Task: Define the EvolutionResult Record

**Parent Workstream**: WS1 - Evolution Layer Core Infrastructure  
**Difficulty**: Small Model Friendly  
**Estimated Context**: < 4k tokens

## Objective
Define a clean `EvolutionResult` record that the evolution layer returns after `Commit()`, containing the final root, analysis result, trace, and success/rollback status.

## Context You Must Read First

- Core Engineering Principles (build working code first).
- "Target Shape" and "Core Contract to Preserve" in `docs/decisions/2026-05-31-evolution-layer-design.md`.
- Look at the old V2 `DomainMutationExecutionResult` for inspiration on fields only (do not copy implementation).

## Exact Steps

1. Create `Poly/DomainModeling/Evolution/EvolutionResult.cs`.
2. Define a simple record with at minimum:
   - `Domain Root`
   - `AnalysisResult Analysis`
   - `EvolutionTrace Trace`
   - `bool Succeeded`
   - `bool RolledBack`
3. Add two static factory methods: `Success(...)` and `RolledBack(...)`.
4. Write a tiny test that constructs both success and rollback results.

## Verification

- [ ] Compiles
- [ ] Small test passes
- [ ] Design is deliberately minimal

## Output

- New `EvolutionResult.cs` file
- One small test
- Mark micro-task complete (via summary)

## Status

**Claimed by**:  
**Status**: Not Started / In Progress / Done (summary submitted)