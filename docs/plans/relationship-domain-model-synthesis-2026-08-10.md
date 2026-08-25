# Relationship domain-model synthesis — drop `Domain.Relationships` storage

**Date:** 2026-08-10  
**Status:** ✅ **EXECUTED 2026-08-10** — entity-owned navigations landed. `Entity.Navigations` is the model truth; `Domain.Relationships` is a computed flatten (never stored); the source-scoped RLM/MTI are synthesized from entity navs; the structural analyzer folds navs into the entity member check; evolution/mutation context operate on entity navs. Kept a **legacy 3-arg `Domain` ctor** that redistributes relationships onto entity copies (so the ~356 existing `new Domain(name, types, rels)` call sites compile unchanged) plus **canonical entity re-resolution** in `DomainEntityInstance.Create` so runtime instances always carry the analysis-canonical entity (node identity preserved). Printer + lowering read `entity.Navigations` directly. Full suite green (1971). **Review fixes (2026-08-10):** bridge `Redistribute` now **appends** to pre-set navs instead of replacing; `ResolveSourceRelationshipOrThrow` lists **all** declaring sources on wrong-source failures; `create in Rel` export arity fixed (CS1501, see [`csharp-export-createin-bugs-2026-08-10.md`](csharp-export-createin-bugs-2026-08-10.md)) with a signature/call-arity guard test.

**Bridge RETIRED 2026-08-10:** the 3-arg `Domain` ctor + `Redistribute` are gone. Production never passed relationships through it (all 5 call sites used `[]`), and the 360 test call sites migrated to `DomainTestFactory.Create` (test assembly, `Poly.DomainModeling` namespace): `Create(name, types, rels)` redistributes + throws on orphan sources; `Create(name, types)` forwards. `Domain` is now strictly `(Name, Types)` — a relationship can only exist on a defined entity, and that invariant is structural (no construction path creates an orphan). Remaining: retire legacy `Relationship` payloads (`SourceOwnsTarget`, rel-level `Properties`/`Stages`/`Policies`); `DomainBuilder` name-keyed relationship dictionary remains a builder-level same-name limitation (example-only usage). Derived-facts hub (back-ref derivation landed via the auto-wire; stage-scoped create-in landed) — see "Derived facts" note below.
**Goal:** Stop storing relationships as a domain-global list. Nav declarations become entity members; the relationship view is a computed flatten (model-level) + analysis-synthesized metadata. Update every consumer and mutation.

---

## Target shape

```
Entity  — owns →  Navigations: IReadOnlyList<Relationship>   (Relationship keeps node identity)
Domain  — computes →  Relationships => Types.OfType<Entity>().SelectMany(e => e.Navigations)
Analysis — synthesizes → RLM (source → nav → Relationship), MTI.RelationshipsByName,
                          RelationshipContractMetadata, topology, subscription plans — from entity navs
```

The nested (source → nav) index shape from the 2026-08-10 source-scoping slice survives; only its build source changes. Back-reference of `(S, name → T)` = "the nav on `T` whose target is `S`" — derived, not stored.

## Consumer inventory (migration targets)

### A. Model-level, no-analysis (read the computed flatten or `entity.Navigations` directly)
| Consumer | Current use | Migrate to |
|----------|-------------|------------|
| `DomainDslPrinter` (`Parsing/DomainDslPrinter.cs:105`) | `domain.Relationships.Where(source == entity)` | `entity.Navigations` directly |
| `DomainQueries.Overview/GetEntity/ListRelationships` | `domain.Relationships` (count, navs, list) | computed flatten (or navs per entity) |
| `DomainToCSharpExporter` null-analysis fallback (`:1320`) | flat list | computed flatten |
| `EffectLoweringPass` null-analysis fallback (`:617`) | flat list | computed flatten |
| `DomainProgramProjection` (`:40`) | `domain.Relationships.ToList()` | computed flatten |
| `Mcp DomainTools` (`get_relationships`, counts, fingerprints, remove-disambiguation, apply_dsl result) | flat list | computed flatten |
| `Mcp RuntimeTool` (link/unlink validation) | flat list | computed flatten (already source-scoped) |

### B. Analysis passes (build/synthesize from entity navs)
| Pass | Current use |
|------|-------------|
| `SemanticDomainAnalyzer` (`BuildRelationshipLookup`) | builds RLM from flat list → **build from `entity.Navigations`**; per-source duplicate diagnostic becomes redundant (entity-member uniqueness) |
| `DomainCatalogPass` (MTI `relationshipsByName`) | builds from flat list → build from entity navs |
| `StructuralDomainAnalyzer` | per-source relationship duplicate group + type-name collision → fold navs into the entity member check; re-scope or drop the type-name collision check |
| `EntityStructureAnalyzer` (ctor params) | `domain.Relationships.Where(source == entity)` → `entity.Navigations` |
| `CrossReferencePass` (topology lookup) | flat list + nested dict → entity navs |
| `EffectTopologyPass`, `OwnershipAggregatePass`, `RuntimeContractAnalyzer`, `CapabilityAnalyzer`, `StorageAnalyzer`, `EffectFactsPass`, `EffectAnalyzer`, `PolicyConstraintAnalyzer`, `SubscriptionAnalyzer` | read flat list / RLM | read `entity.Navigations` or the synthesized RLM |

### C. Mutations (reshape to entity-nav mutations)
| Mutation | Current | Target |
|----------|---------|--------|
| `DomainMutationContext.Relationships` list | copy/add/remove/update | operate on `entity.Navigations` |
| `AddRelationshipChange` | `context.Relationships.Add` | `AddNavigationToEntityChange` |
| `RemoveRelationshipChange` | remove by (source, name) | remove nav from entity |
| `SetRelationshipShapeChange` | update flat rel | update nav on entity |
| relationship-content changes (props/stages/policies on `Relationship`) | update flat rel | update nav node (keep payloads until retirement decision) |
| `DomainEvolution` builder methods | `(sourceEntityName, relName, …)` | entity-scoped nav methods — `sourceEntityName` becomes redundant |
| Mcp `add`/`remove` relationship | `AddRelationship(source, target)` / `RemoveRelationship(source, name)` | entity nav mutation |

## Migration bridge (de-risk the 356 `new Domain(name, types, rels)` test sites)

`Domain` currently is a positional record `Domain(Name, Types, Relationships)`. Options:
- **Bridge (recommended):** make `Domain` a record with `(Name, Types)` positional + a **normalizing constructor** `Domain(name, types, relationships)` that redistributes each relationship onto its source entity's `Navigations` and drops the stored list. All ~356 `new Domain(...)` call sites keep compiling unchanged; tests asserting `Relationships.Count` keep working because the computed flatten returns the same nodes. The bridge is removed in a later cleanup phase once direct `Entity.Navigations` construction is the norm.
- **Bulk churn:** change the signature and rewrite all call sites. Higher diff, faster end-state. Choose if the bridge's shim cost outweighs the churn.

`Relationship` keeps its node identity under the entity, so metadata keying and incremental-analysis tree visits keep working (nodes just move parent).

## Phases (each ends with the full suite green)

1. **Model:** add `Entity.Navigations`; make `Domain.Relationships` a computed flatten; add the normalizing ctor bridge; move `Relationship` from `Domain.Children` to `Entity.Children`. Update the incremental tree expectations.
2. **Builders:** `SemanticDomainAnalyzer` + `DomainCatalogPass` build from entity navs; `StructuralDomainAnalyzer` folds navs into the entity member duplicate check; remove the slice's now-redundant per-source relationship duplicate diagnostic.
3. **Analysis consumers:** migrate passes B to `entity.Navigations` / synthesized RLM. Delete the null-analysis flat-list fallbacks where analysis is now guaranteed.
4. **Model-level consumers:** migrate group A to `entity.Navigations` / computed flatten.
5. **Mutations:** reshape group C; drop the redundant `sourceEntityName` from `DomainEvolution` relationship methods; update MCP `add`/`remove`.
6. **Cleanup:** retire or quarantine legacy `Relationship` payloads (`SourceOwnsTarget`, rel-level `Properties`/`Stages`/`Policies`) if no product caller exists (§3); remove the bridge ctor; delete the type-name-collision check if re-scoping shows it's a printer-only artifact.

## New tests

- Entity-owned navs round-trip (parse → print → re-parse) with same-name navs on different sources.
- Back-reference derivation: given `(S, name → T)`, resolve the nav on `T` pointing to `S`.
- `create in Rel` auto-wire (target's back-ref set to the creating instance) once back-ref derivation lands.
- Mutation surface: `add`/`remove` relationship against entity-owned navs; same-name removal with `source` disambiguation.
- Incremental-analysis invalidation over the new tree shape (relationship under entity).

## Risks

- **Tree-shape change** (relationship parent moves) affects incremental-analysis invalidation and any pass iterating `Domain.Children`. Mitigate by keeping `Relationship` nodes and only moving their parent.
- **Constructor churn** (356 sites) — mitigated by the bridge.
- **Double view period:** computed flatten (model) + synthesized metadata (analysis) both exist. They must never disagree — both derive from the same entity navs, so disagreement is impossible by construction.
- **Legacy payloads** on `Relationship` linger if retirement is deferred; acceptable while the node stays.

## Open decisions

1. ~~Bridge ctor vs bulk call-site churn (Phase 1)~~ → **Bridge used**; retire the bridge in the cleanup phase.
2. Keep or re-scope the relationship-vs-type-name collision check (Phase 2/6) → **kept**, iterating entity navs.
3. Retire `SourceOwnsTarget`/rel-level `Properties`/`Stages`/`Policies`, or promote to metadata (Phase 6).

## Derived facts (next phase — enabled by this refactor)

With relationships entity-owned, cross-entity facts are computed once in a single derived-facts hub instead of being re-derived per pass:

- **Back-reference of `(S, name → T)`** = the nav on `T` whose target is `S`. Consumers today hand-roll this: `OwnershipAggregatePass` (back-ref scan), `CrossReferencePass` (inverse-pair heuristic), exporter `create in Rel` auto-wire.
- **Stage-scoped child creation** `(S, stage, nav, childType)` — `EffectTopology.CreateInRelation` currently carries `ActionName` but not the owning stage.
- **Dependency/aggregate edges** `S → T` per nav.

Boundary: link *presence* and quantifier truth over linked instances are runtime/store facts, never analysis-derivable. Each new fact needs a named consumer before it is published (§3).
