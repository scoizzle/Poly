# Micro-Task: Sketch a Fluent Evolution API Surface

**Parent Workstream**: WS1 - Evolution Layer Core Infrastructure  
**Difficulty**: Small Model Friendly (design + documentation task)  
**Estimated Context**: Low

## Objective
Create a concrete proposal (as a document or spike code) for a fluent, builder-like public API on top of the Evolution layer that could be used for both initial domain creation and incremental changes.

## Context You Must Read First

- `docs/decisions/2026-05-31-evolution-layer-design.md` (especially the new section "Can the Evolution API Itself Be Made Ergonomic Enough...")
- `docs/plans/v2-to-v3/spikes/fluent-evolution-api-proposal.md`
- The current `DomainEvolution` (Apply + Evolve() builder) API after the removal of the explicit transaction model (see 2026-05-31-evolution-layer-design.md).
- The existing `DomainBuilder` usage in `PersonLifecycleViaBuilders.cs` (for comparison).

## Exact Steps

1. Read the proposal document and the existing spike.
2. Pick one small slice of the ECommerce domain (e.g., just the Order entity + one action + one simple effect).
3. Write a side-by-side comparison:
   - How it would look using the current V3 `DomainBuilder`.
   - How it could look using a hypothetical fluent evolution API (e.g. `domain.Evolve().AddEntity("Order")...`).
4. Document pros/cons of the fluent evolution style vs the current builders.
5. Propose 3-5 specific API methods that would make the evolution path feel natural (e.g. `WithProperty`, `AddAction`, fluent effect configuration).

## Verification

- [ ] The comparison document is clear and saved in the spikes folder.
- [ ] At least one concrete API shape is proposed that could be prototyped later.
- [ ] The write-up explicitly considers the goal of making the evolution surface the primary agent interface.

## Output

A new or updated document in `docs/plans/v2-to-v3/spikes/` with the comparison and API proposal.

## Status

**Claimed by**:  
**Status**: Superseded (2026-07-10) — evolution foundation delivered; see master-roadmap.md