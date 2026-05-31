# Workstream WS1: Evolution Layer Core Infrastructure

**Phase**: 1  
**Priority**: Critical Path  
**Owner**: TBD  
**Status**: Not Started  
**Last Updated**: 2026-06-01

## Goal

Create the foundational classes and mechanisms for the thin evolution layer on the immutable V3 core (after the decision to drop the explicit transaction/commit model):
- `DomainEvolution` (with `Apply(batch)` and `Evolve()` fluent builder entry points)
- `EvolutionResult`
- `EvolutionTrace`
- Basic applicator logic that produces new immutable `Domain` roots (via V3 builders or pure construction helpers)
- The analysis gate + rolled-back result path

See `docs/decisions/2026-05-31-evolution-layer-design.md` (resolved Open Question #8) for the rationale: the full `EvolutionTransaction` / `BeginTransaction` / `Commit` ceremony is not required on immutable records.

## Entry Criteria
- V3 immutable core is stable and building cleanly.
- Core Engineering Principles have been reviewed.
- Relevant decisions (immutable core + evolution design) have been read.

## Key Tasks

1. **Design the public API surface** (align with the simplified model in `2026-05-31-evolution-layer-design.md`)
   - `DomainEvolution(current: Domain)`
   - `Apply(changes, priorAnalysis?)` batch form
   - `Evolve()` → `EvolutionBuilder` (lightweight fluent accumulator)
   - Builder finalizer: `Apply(priorAnalysis?)` which runs the full gate and returns `EvolutionResult`

2. **Implement basic change representation**
   - Start with a simple `DomainChange` abstract record + concrete subtypes for the MVP operations.
   - Or build an adapter layer over existing `DomainMutationIntent` (preferred for early compatibility).

3. **Build the applicator**
   - Core logic that takes a previous immutable root + list of changes → produces a new immutable root.
   - Primary implementation path: use V3 fluent builders internally.

4. **Analysis gate + rolled-back result**
   - Run analysis on the proposed new root.
   - On any error diagnostics → return a result whose `Root` is the *original* snapshot, `Succeeded = false`, `WasRolledBack = true`, plus full diagnostics and trace.

5. **Trace generation**
   - Produce `EvolutionTrace` containing steps, affected nodes, timing, rolled-back flag, etc.
   - Make it useful for agents (clear, structured).

6. **Basic tests**
   - Simple successful evolution (new root returned).
   - Analysis error case that returns the original root + rich diagnostics + WasRolledBack = true.

## Exit Criteria
- Agents can call `new DomainEvolution(domain).Apply(changes)` or use the fluent `domain.Evolve().AddX()... .Apply()`.
- Successful evolution returns a new immutable `Domain` root.
- Failed analysis returns the *original* root + full diagnostics + `WasRolledBack = true` + rich trace.
- Clean build + passing basic tests.
- Decision record created/updated for the removal of the transaction model and the resulting simpler API shape.

## Dependencies
- None (foundational for Phase 1)
- Outputs interfaces that WS2 and WS3 will consume.

## Parallelism Notes
This workstream should be claimed by one primary agent. Other agents can work on WS2–WS6 in parallel as long as they coordinate on interfaces via this workstream's owner.

## Related Documents
- `docs/decisions/2026-05-31-evolution-layer-design.md`
- `docs/decisions/2026-core-engineering-principles.md`
- Future: NodeId continuity decision (WS2)

## Current Blockers / Open Questions
- Preferred initial change representation (`DomainChange` vs direct `DomainMutationIntent` adapter)?
- Depth of "with" helpers vs builder-only updates?