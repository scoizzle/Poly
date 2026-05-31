# Micro-Task: Implement Basic DomainEvolution + EvolutionBuilder Skeleton (Simplified)

**Parent Workstream**: WS1 - Evolution Layer Core Infrastructure  
**Difficulty**: Medium (requires understanding the design)  
**Estimated Context**: Medium

> **Important (June 2026):** This task was written before the decision documented in `docs/decisions/2026-05-31-evolution-layer-design.md` (resolved Open Question #8). The explicit `EvolutionTransaction` / `BeginTransaction` / `Commit` model has been removed. The new target is `DomainEvolution` with `Apply(batch)` + `Evolve()` returning a lightweight `EvolutionBuilder`.
>
> The **spirit** of the task remains: prove that the analysis gate + `EvolutionResult` (success vs. rolled-back with original root + trace) works cleanly on the immutable core. Update the task details to the simpler API before starting.

## Objective
Create a minimal but working skeleton for `DomainEvolution` (with `Apply` and `Evolve()` fluent builder) that can handle a no-op batch (apply nothing → run analysis → succeed or return rolled-back result).

## Context You Must Read First

- `docs/decisions/2026-05-31-evolution-layer-design.md` (Core Contract and Target Shape)
- `docs/decisions/2026-core-engineering-principles.md`
- Current skeleton files in `Poly/DomainModeling/Evolution/` (if they exist)
- How V3 analysis works (`DomainModelAnalyzer`)

## Exact Steps (Updated for Simplified Model)

1. Create (or clean up) `DomainEvolution.cs` with:
   - Constructor taking current `Domain` + optional analyzer
   - `Current` property
   - `Apply(IReadOnlyList<DomainChange>, priorAnalysis?)` batch method
   - `Evolve()` method returning an `EvolutionBuilder`

2. Implement (or clean up) the lightweight `EvolutionBuilder` (can be in the same file initially) with:
   - `Apply(DomainChange)` to accumulate
   - `Apply(AnalysisResult? prior = null)` finalizer that delegates to `DomainEvolution.Apply`

3. Make the final `Apply` path always run analysis (even on empty change list) and demonstrate both:
   - Success path returning a new (or same) root with `Succeeded = true`
   - Error path (inject a diagnostic) returning the *original* root with `WasRolledBack = true`, diagnostics, and trace

4. Delete or tombstone `EvolutionTransaction.cs` (add Obsolete + pointer to the decision doc).

4. Add clear TODOs / placeholders for the real `ApplyChanges` logic.

## Verification

- [ ] Compiles cleanly
- [ ] Can call `new DomainEvolution(domain).BeginTransaction().Commit()` 
- [ ] Successful commit returns `EvolutionResult` with `Succeeded = true`
- [ ] If you force an analysis error, it returns `RolledBack = true`
- [ ] Rich `EvolutionTrace` is produced in both cases

## Output

- Working (if minimal) `DomainEvolution` and `EvolutionTransaction` classes
- Basic tests demonstrating success and rollback paths
- Clear placeholders for the real applicator logic

## Status

**Claimed by**:  
**Status**: Not Started / In Progress / Done (summary submitted)