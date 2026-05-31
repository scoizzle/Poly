# Workstream WS7: V3 Expressiveness Audit (Phase 1)

**Phase**: 1 (parallel with WS1)  
**Priority**: High (risk reduction for Phase 4)  
**Owner**: TBD (light support from WS1 owner initially)  
**Status**: Not Started  
**Last Updated**: 2026-06 (created under active ownership)

## Goal

Produce a living, honest catalog of exactly what the current V3 immutable model (`Poly/DomainModeling/`) can and cannot express compared to the production V2 surface (`Poly/Data/Modeling/`).

This prevents Phase 4 ("Full Expressiveness + Roadblock Resolution") from becoming a reactive scramble when real roadblock scenarios surface.

## Why This Matters Now (Phase 1)

The 2026 code review notes in `master-roadmap.md` identified multiple concrete gaps during the initial port planning. These were treated as "forcing functions" in the immutable-core decision but were never catalogued in one place. Without this audit, Phase 4 scope will be discovered rather than planned.

## Deliverable

A single living document (this file or a linked appendix) containing:

- A table of every significant V2 modeling concept.
- V2 status vs V3 status (with file references where possible).
- Classification: Intentional simplification (cleaner on V3) vs. genuine gap vs. deferred to later phase.
- Notes on whether the gap blocks any known demo or roadblock scenario.
- Recommendations for Phase 4 prioritization.

## Initial Seed Catalog (from Code Review + Direct Inspection)

| Concept | V2 Status | V3 Status | Classification | Notes / Roadblock Impact |
|---------|-----------|-----------|----------------|--------------------------|
| Entity inheritance (`ParentEntity`) | ✅ `Entity.cs:47` | ❌ Not present | Gap | May affect stage hierarchy modeling |
| Actor entity subtype (first-class principal with identity profile) | ✅ `Actor.cs` + dedicated mutation commands + rules | ❌ No `Actor` type | Gap | Access-control / UAC scenarios blocked |
| Rich Rule subtypes on Policy | ✅ `Policy._rules` + 6+ subtypes (`CrossPropertyRule`, `ActorTypeRule`, `ActorRoleRule`, `ActorPropertyRule`, `CompositeRule`, etc.) + JSON polymorphism | ⚠️ `Policy` uses single `DomainExpression` only | Intentional simplification (cleaner) + partial gap | Actor-aware rules and composite rules not expressible today |
| Event subscriptions with correlation | ✅ `EventSubscription` + `EventCorrelationBinding` + full mutation support | ❌ Basic events only (publish via effects) | Gap | Complex event-driven workflows blocked |
| Relationship-scoped stages / policies / actions | ✅ `Relationship` carries independent `_stages`, `_policies`, etc. | ❌ `Relationship` has only properties + cardinality + type refs | Gap | Relationship-centric behavior (e.g. certain ownership or linking workflows) not modelable |
| Advanced effects (Composite, Conditional, InvokeAction, LinkRelationship, etc.) | ✅ Full hierarchy under `Effects/` + mutation commands | ⚠️ Only `CreateEntityInstance`, `PublishEventEffect`, `StageTransitionEffect` | Gap for MVP | Many real demos use composites/conditionals |
| Imported contracts + bindings | ✅ `ImportedContract`, `ContractBinding`, `ContractEndpoint`, etc. + recipes | ❌ Not present | Gap | Contract import / interop scenarios |
| Dynamic calculations / cross-entity expressions | Partial (via `ExpressionValue` in some places) | ⚠️ `DomainExpression` is intentionally minimal (property/param/owned/literal/exists/notexists + basic ops) | Intentional minimalism; roadblock forcing function | Library `RenewLoan` (dynamic calc) and similar scenarios are the test |
| Ownership constraints / first-class owned collections | Convention + `OwnsOne` builder sugar + `OwnedAccess` expr | Same convention + expr navigation | Mostly equivalent | Some advanced ownership roadblocks remain per decisions |

**Sources for the above**: Direct code inspection (V2 `Data/Modeling/` vs V3 `DomainModeling/`), the 2026-05-30 code review notes in master-roadmap, and the immutable-core decision records.

## Tasks

1. Expand the table above with every remaining V2 concept (full sweep of V2 model types, effects, rules, subscriptions, visual metadata, etc.).
2. For each gap, note whether it is required by any existing demo in `Poly.Benchmarks/DomainModeling/` or the documented roadblock scenarios (library, ecommerce, healthcare).
3. Classify each as: "Intentional (keep V3 cleaner)", "Must port in Phase 4", or "Needs new design on immutable + expr foundation".
4. Link to (or create) decision records for any non-obvious modeling choices on the V3 side.
5. Keep this document updated as WS1 and WS5 discover new gaps during implementation.

## Exit Criteria

- Comprehensive table exists and is referenced from the master roadmap and Phase 4 planning.
- At least the known roadblock scenarios (Library RenewLoan, etc.) are explicitly mapped to gaps or "works on current V3".
- WS1 owner has reviewed and confirmed the audit is sufficient to de-risk Phase 4 scoping.

## Parallelism

This work can (and should) run in parallel with WS1 applicator development. It is documentation + analysis heavy rather than code heavy — suitable for a support or hygiene agent.

## Related

- `docs/decisions/2026-05-31-immutable-core-domain-modeling.md` (roadblocks as forcing functions)
- Master roadmap code review notes (2026-05-30)
- WS5 (Proof on Examples) — this audit should directly inform which roadblock to prove first in Phase 1

---

**Owner note**: This workstream exists because the 2026 ownership plan and code review explicitly called it out as missing. Do not let Phase 4 become a surprise discovery exercise.