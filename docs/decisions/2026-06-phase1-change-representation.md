# Decision: Phase 1 Change Representation Strategy (Native DomainChange + Builder-backed Applicator)

**Date:** 2026-06  
**Status:** Draft (under WS1 ownership)  
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

- Four initial change types implemented and working (Add/Remove Entity, Add/Remove Property on Entity).
- More operation types will be added as narrow, testable records + applicator cases.
- A follow-up decision (or update to this one) will be needed before Phase 3 if we decide to evolve the intent surface for agents.

## Next Steps

- Keep the native `DomainChange` hierarchy as the source of truth for the applicator.
- Document any adapter strategy in WS1 or a later decision when MCP integration work begins.
- Revisit after WS5 (proof on real examples) whether the surface feels good enough for agents or needs ergonomic sugar.

This decision will be finalized once the first end-to-end proof (PersonLifecycle slice via evolution) is green.