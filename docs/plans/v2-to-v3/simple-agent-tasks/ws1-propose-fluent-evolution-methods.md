# Micro-Task: Propose Specific Fluent Methods for the Evolution API

**Parent Workstream**: WS1 - Evolution Layer Core Infrastructure  
**Difficulty**: Small Model Friendly  
**Estimated Context**: Low–Medium

## Objective
From the fluent evolution proposal, identify and document a small, high-value set of fluent methods (e.g. `WithProperty`, `AddAction`, `WithEffect`, etc.) that would give the biggest ergonomic win for common modeling tasks.

## Context You Must Read First

- `docs/decisions/2026-05-31-evolution-layer-design.md` (the section on making Evolution ergonomic)
- `docs/plans/v2-to-v3/spikes/fluent-evolution-api-proposal.md`
- A slice of one of the real demos (e.g. how `Order` + `PlaceOrder` action is modeled in ECommerceDomain.cs)

## Exact Steps

1. Pick 4–6 common modeling operations from the ECommerce or Library demo (e.g. adding an entity with properties, adding an action with parameters and one effect, attaching a policy).
2. For each, write the "ideal" fluent syntax you would want an agent to be able to write against the evolution layer.
3. Document the minimal set of builder-style methods that would be needed on a `FluentEvolutionBuilder` / `FluentEntityBuilder` etc. to support those examples.
4. Note any places where the analysis gate, trace, and rolled-back result behavior should be visible or hidden in the fluent API. (There is no longer an explicit transaction object.)

## Verification

- [ ] The proposed method list is concrete and prioritized (top 5–7 methods).
- [ ] The write-up discusses how these methods would still produce proper `EvolutionTrace` entries and participate in the analysis gate.
- [ ] Saved in the spikes folder.

## Output

A short document or addition to the proposal spike listing the recommended first fluent methods for the Evolution API, with examples.

## Status

**Claimed by**:  
**Status**: Not Started / In Progress / Done (summary submitted)