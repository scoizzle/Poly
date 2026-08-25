# Decision: Phase 1 Change Representation Strategy (Native DomainChange + Builder-backed Applicator)

**Date:** 2026-06  
**Status:** Accepted  
**Deciders:** Owner (Grok) + review

## Context

The V2→V3 port requires a thin evolution layer that delivers the same analysis-gated, traceable, rollback-on-error experience that agents currently get from V2's heavy `DomainMutationCommand` + `DomainMutationIntent` machinery — but on top of true immutable records with far lower incidental complexity.

The evolution design doc left open the question of initial change representation.

## Decision

For Phase 1 we use a small set of native `DomainChange` sealed record subtypes as the primary currency inside the applicator.

- The applicator interprets these records to produce new immutable `Domain` roots.
- Implementation inside the applicator primarily uses the existing V3 fluent builders (or direct record construction with `with` expressions) rather than inventing new mutation primitives.
- A thin compatibility adapter (or direct support for the highest-frequency `DomainMutationIntent` shapes) will be added early enough for MCP tool continuity during the transition.
- We deliberately do **not** attempt a 1:1 port of all ~50 V2 intents in Phase 1.

This matches the "evolution on top of builders" layering decision and the "build working code before abstractions" principle.

## Rationale

- Native records are simple, strongly typed, inspectable, and align with the immutable core.
- Keeps the first working version small and fast to deliver (the real goal of Phase 1).
- Avoids re-creating the mutation tax in a new form.
- The anti-pattern guidance (structured/inspectable changes, good NodeId behavior) is easier to satisfy with explicit record types from day one.

## Consequences

- Full set of MVP `DomainChange` subtypes implemented and working end-to-end: Add/Remove for Entity, PrimitiveType, Event, ValueType, Property on Entity, Stage (with simple parent), Action on Entity and Stage, Relationship, plus attachment of Policies, Parameters, Effects (Create with bindings, PublishEvent with bindings, StageTransition, OnEntry/OnExit), and result setting.
- Applicator uses direct record `with` updates + context for efficient batch + NodeId preservation.
- Traces now include overall and per-step AffectedNodeIds.
- The first end-to-end PersonLifecycle-style slice is constructible purely via `DomainEvolution` + changes (see proof test in Poly.Tests).
- Interfaces (DomainChange, EvolutionResult, EvolutionTrace, EvolutionBuilder) are stable enough to hand off to WS4 (traces/UX) and WS5 (full proofs).

## Next Steps

- Keep the native `DomainChange` hierarchy as the source of truth for the applicator.
- WS1 complete; handoff to WS4/WS5 for richer trace UX and full roadblock examples.
- Revisit adapter/MCP surface or fluent ergonomics in later phases only if real first consumers (agents) demonstrate need.

WS1 proof complete; this decision is now accepted.

**Post-acceptance implementation refinement (WS4):** As part of aggressive simplification of the tracing model, the `AffectedNodeIds` (both overall and the earlier per-step variant) were removed from `EvolutionTrace`, `EvolutionStep`, `DomainMutationContext`, and all `DomainChange` implementations. The list provided no incremental-analysis value in the current MVP (GetAffectedNodes still returns empty) and duplicated work already questioned by "do we really need to examine what nodes are impacted?". The lean model (natural descriptions owned by changes + Information diagnostics for history + minimal structured trace) is the current truth. The core decision to use native `DomainChange` records remains unchanged.