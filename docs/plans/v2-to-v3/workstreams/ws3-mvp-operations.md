# Workstream WS3: MVP Operation Support

**Phase**: 1  
**Priority**: High  
**Owner**: TBD  
**Status**: Not Started  
**Last Updated**: 2026-06-01

## Goal
Implement a useful initial set of operations that can be performed through the evolution layer. This is what will actually make the layer valuable to agents and demos in the short term.

## Proposed MVP Scope (to be confirmed)

Focus on operations that are commonly used in the current demos and that help solve roadblocks:

- Add/Remove basic types (Entity, Actor?, Primitive, Event, ValueType)
- Add/Remove Property on Entity
- Add/Remove Stage (including simple parent relationships)
- Add/Remove Action on Entity/Stage
- Add/Remove simple Effects on Actions (CreateEntityInstance with PropertyBindings, PublishEventEffect, StageTransitionEffect)
- Attach Policies using `DomainExpression`
- Basic Relationship add/remove

**Out of scope for MVP** (defer to later phases or later in Phase 1):
- Advanced effects (Composite, Conditional, InvokeAction, LinkRelationship, etc.)
- Actor identity/claims configuration
- Event subscriptions and routing
- Imported contracts and bindings
- Visual metadata

## Entry Criteria
- WS1 has basic transaction + commit working (even with limited operations).
- Decision made on initial change representation (intent adapter vs new `DomainChange` types).

## Key Tasks

1. **Confirm exact MVP operation list** (coordinate with WS5 for the proof scenarios).
2. Implement each operation through the evolution layer (using builders under the hood).
3. Add corresponding support in the trace generation.
4. Write focused tests for each operation + rollback cases.
5. Document usage patterns (especially how `DomainExpression` is used for policies, guards, and effect bindings).

## Exit Criteria
- The agreed MVP set of operations works end-to-end through the simplified `DomainEvolution.Apply` / fluent `Evolve()` surfaces (explicit EvolutionTransaction removed).
- At least one multi-step batch with rollback on error is demonstrated.
- Clean integration with WS1 and WS4 (traces).
- Usage examples exist that can be used by WS5.

## Dependencies
- WS1 (core layer)
- Partial dependency on WS2 if NodeId behavior affects the operations being tested.

## Parallelism Notes
Once WS1 exposes a stable way to register new operations, multiple agents can implement different operations in parallel (e.g., one agent owns "Stage + Action lifecycle", another owns "Effect attachment", etc.).

## Verification
- Unit tests per operation family.
- End-to-end batch test that mixes several operation types.
- Successful execution of the chosen roadblock scenario (via WS5).

## Related
- `docs/decisions/2026-05-31-evolution-layer-design.md`
- Roadblock documents (ecommerce-roadblocks.md, library-roadblocks.md, healthcare-roadblocks.md)