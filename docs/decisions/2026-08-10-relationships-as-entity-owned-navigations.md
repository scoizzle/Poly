# Relationships as Entity-Owned Navigations (Synthesized Domain View)

**Date:** 2026-08-10
**Status:** Accepted (implemented 2026-08-10)
**Deciders:** Architecture team
**References:** [`relationship-name-source-scoping-2026-08-10.md`](../plans/relationship-name-source-scoping-2026-08-10.md), [`csharp-export-createin-bugs-2026-08-10.md`](../plans/csharp-export-createin-bugs-2026-08-10.md), [`relationship-domain-model-synthesis-2026-08-10.md`](../plans/relationship-domain-model-synthesis-2026-08-10.md)

---

## Context

The DSL authors relationships as **entity-inline navigation properties** (`orders: many Order` on `Customer`). There is no domain-global relationship reference syntax anywhere in the product surface — policies (`any orders where …`), subscriptions (`when orders Active`), `create in orders`, and `invoke orders.X` are all authored *inside* the source entity.

The IR, however, stores relationships as a **flat, domain-global list** (`Domain.Relationships`) with a globally unique name. Every semantic consumer treats them as source-owned and re-derives source-sideness after the fact:

- Runtime validates `relationship.Source.TypeName == instance.Entity.Name` before using a link; store edges are `(name, source, target)` tuples.
- The 2026-08-10 source-scoping slice found ~10 hand-maintained name-keyed indexes (`RelationshipLookupMetadata`, mutation index, `FindRelationship`, subscription plans, runtime, exporter, lowering, parser).
- The `IsBackReference` flag only fires for **self-relationships** — the model has no way to express "this nav on the target is the back-end of relationship X." That is the root cause of the dead `create in Rel` auto-wire (DSL guide §0.3 claims it; export passes `null`).
- The CS1501 export drift came from consumers hand-rolling the same relationship facts in ways that must stay in lockstep.

The mismatch is structural: **the stored model shape does not match what the language can express or what the runtime requires.**

## Decision

**A relationship is a navigation property owned by its source entity. The domain-global relationship list is a derived view, not stored state.**

1. **Model truth = entity nav members.** `Entity` gains `Navigations: IReadOnlyList<Relationship>`. The `Relationship` node keeps its node identity but its parent becomes the owning entity (tree: `Domain → Entity → Relationship`). Name uniqueness falls out of ordinary entity-member uniqueness — no global-name invariant exists anywhere.
2. **`Domain.Relationships` is a computed flatten** of `Types.OfType<Entity>().SelectMany(e => e.Navigations)`, for the model-level consumers that must work without analysis (printer, queries, export/lowering null-analysis fallbacks, MCP listing). It is derived, so it carries no naming invariant of its own.
3. **The relationship *semantic* view is synthesized by the analysis pipeline** into metadata — source-scoped index (source → nav name → relationship), relationship contracts, topology, subscription dispatch — built from entity navs. The nested index shape from the 2026-08-10 slice survives unchanged; only its *source* changes from the flat list to entity members.
4. **Back-references are derived**, not stored: the back-end of relationship `(S, name → T)` is the nav on `T` whose target is `S`. This gives the `create in Rel` auto-wire a principled home.

The relationship node shape is chosen so metadata keyed on `Relationship` keeps working and analysis passes that visit relationship nodes keep their tree visits (they now walk under the entity).

## Consequences

**Positive:**
- Name scoping becomes structural — the parser's per-source duplicate set, the structural per-source check, and the semantic per-source diagnostic from the slice all collapse into entity-member uniqueness.
- Back-reference detection is a derived-edge computation, not a stored flag → enables the `create in Rel` auto-wire fix.
- One place holds relationship facts (entity members) → kills the CS1501 hand-rolled-scan drift class.
- The model stops lying: stored shape matches the DSL surface.
- All ~10 name-keyed indexes either disappear or become single-source-of-truth synthesis.

**Negative:**
- Wide blast radius: `Domain` node shape changes; `Entity.Children` and `Domain.Children` change → incremental-analysis tree invalidation sees new shapes.
- `new Domain(name, types, relationships)` is called ~356 times in tests — needs a normalization bridge (see plan) or bulk churn.
- Evolution/mutation surface reshapes: relationship mutations become entity-nav mutations; the `sourceEntityName` parameter threaded through `DomainEvolution` in the slice becomes redundant.
- Legacy `Relationship` payloads (`SourceOwnsTarget`, rel-level `Properties`/`Stages`/`Policies`) have no DSL authoring surface today — they either become metadata or are retired (§3 decision, deferred to the plan's cleanup phase).

## Compliance

- `Domain.Relationships` is never stored; all consumers read the computed flatten or the synthesized analysis view.
- Entity-member name uniqueness is the only name invariant for navigations.
- DSL authoring surface unchanged; runtime semantics unchanged.
- `docs/CORE.md` domain section and the DSL guide updated in the same change.
- Execution roadmap and consumer inventory: [`relationship-domain-model-synthesis-2026-08-10.md`](../plans/relationship-domain-model-synthesis-2026-08-10.md).
