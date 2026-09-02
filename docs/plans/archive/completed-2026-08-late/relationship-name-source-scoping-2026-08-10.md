# Relationship names scoped to source entity (drop domain-wide uniqueness)

**Date:** 2026-08-10  
**Status:** ✅ **EXECUTED 2026-08-10** — Option A landed as a vertical slice. Relationship identity is now (source entity, name) across parser → semantic RLM → catalog MTI → structural analyzer → runtime → exporter → lowering → evolution → MCP (`remove` relationship accepts optional `source`; `link_instances`/`unlink_instances` and `describe_domain_element` disambiguate by instance/entity). Deviations from the proposal: added a structural check that a relationship name must not collide with a type name (preserves the pre-change type/relationship namespace separation), and `CrossReferencePass`'s topology relationship lookup was source-scoped alongside the RLM/MTI. Tests: `SameNameRelationshipSourceScopingTests` (parse/analyze/runtime/export/round-trip) + reworked `N1NavigationTests` + updated evolution/export tests. CORE + DSL guide updated in the same change.
**Problem:** a navigation name used by one entity is reserved for the entire domain. Modeling two children that both point at a parent named `order` fails:

```poly
OrderLine: entity { Sku: Text required  order: Order }
Note:      entity { Body: Text           order: Order }   // parse error
```

```text
Parse error: Relationship 'order' is defined more than once. Relationship names must be unique within a domain. (line …)
```

Back-reference navs named after the parent are the norm, so this is a real modeling wall — and it forced renames in the dogfood `Orders` domain (`owningOrder`).

---

## Why the constraint exists today

Relationship *name* is used as a **domain-global key** in several indexes. All of them assume one relationship per name:

| Consumer | Location | Key |
|----------|----------|-----|
| Semantic `RelationshipLookupMetadata` | `SemanticDomainAnalyzer.cs:58-60` | `GroupBy(Name)` → `dict[name]` (last wins) |
| Mutation index `RelationshipsByName` | `DomainCatalogPass.cs:72-74` | `GroupBy(Name)` → `dict[name]` (last wins) |
| `DomainMutationContext.FindRelationship(name)` | `DomainMutationContext.cs:250-251` | name only |
| Subscription dispatch plan `ByRelationshipName` | `DomainModelMetadata.cs:53` | name only (per stage/entity) |
| Runtime outbound resolution | `DomainEntityInstance.cs:1071` → `TryGetRelationship(Domain, name)` | name only, **then** a source check at `:1076` that throws if the caller isn't the source |
| Export `ResolveRelationship` | `DomainToCSharpExporter.cs:1309-1318` | name lookup + source filter |
| Lowering `ResolveRelationship` fallback | `EffectLoweringPass.cs:617` | name-only `FirstOrDefault` |

If the parser relaxes uniqueness without changing these, duplicate names silently collapse (`GroupBy(...).Last()`) and the runtime/analyzer resolve the *wrong* relationship — violating the fail-closed posture.

## The DSL already scopes relationship references by source

Every place a relationship name appears in authoring is **inside** the source entity (or its stages/actions/policies):

- `any orders where …` / `count orders` — policy on the source
- `when orders Active` — subscription on the source (subscriber)
- `create in orders { … }` — action on the source
- `invoke orders.Process` — action on the source

So the correct identity is **(source entity, relationship name)**. Relaxing to per-source uniqueness does not change any authoring surface — it only removes a false restriction.

The runtime already *enforces* source-sideness (`DomainEntityInstance.cs:1076`) and the store keeps links as `(name, source, target)` tuples — source-scoping is the semantics the code already half-assumes.

## Recommended approach — Option A: source-scoped identity everywhere

Relationship key becomes `(SourceEntityName, Name)`, shaped as the nested dict already used by the mutation index (`StagesByEntity`, `ActionsByEntity`, `EntityPoliciesByEntity`):

```csharp
IReadOnlyDictionary<string /*source entity*/, IReadOnlyDictionary<string /*nav name*/, Relationship>>
```

Migration checklist:

1. **Parser** (`PolyDslParser.cs:1042`) — mirror the existing per-entity `_entityPropertyNames` pattern: `HashSet<string> _relationshipNames` → `Dictionary<string, HashSet<string>>` keyed by source entity. The per-entity property/nav collision check (`:1037`) already covers same-entity conflicts.
2. **Semantic `RelationshipLookupMetadata`** (`SemanticDomainAnalyzer.cs:58-60`, `DomainModelMetadata.cs:21`) — nested-dict key.
3. **Mutation index `RelationshipsByName`** (`DomainCatalogPass.cs:72-74`, `DomainModelMetadata.cs:70`) — nested-dict key.
4. **`TryGetRelationship`** (`DomainSemanticLookupExtensions.cs:206-227`) — add a source-entity parameter; all name-only callers pass the enclosing entity:
   - runtime `DomainEntityInstance` → `this.Entity.Name` (outbound navs; already source-validated)
   - exporter → `sourceEntityName` (already has it)
   - lowering → the Subject's entity
   - subscription → the subscriber entity (plan entry lives on the subscriber)
5. **`DomainMutationContext.FindRelationship`** (`DomainMutationContext.cs:250`) — add source scope; `DomainChange.cs` link/unlink callers have the source instance.
6. **Exporter/lowering lookups** — already source-scoped after the name lookup; they just need the source-scoped index so duplicates survive.

Shape is consistent with the repo's existing nested per-entity indexes — no new abstraction (§6).

## Rejected options

- **Option B — auto-qualify internally (`SourceEntity.order`).** Hides the collision instead of scoping it; export method names and any consumer that prints relationship names get mangled. Violates domain fidelity (§1).
- **Option C — relationship alias syntax (`order as lineParent: Order`).** Adds DSL surface, and the common name still can't be reused without ceremony. Worse ergonomics for the exact case that motivated this.
- **Option D — relax parser only, leave name-global indexes.** Duplicate names collapse silently (`Last()` wins) → wrong-relationship resolution at runtime. Violates fail-closed; not acceptable.

## Follow-ups

- Decide Option A vs alternatives; if A, implement as a vertical slice (parser → semantic → catalog → runtime → exporter) with a test that models two entities both with a back-ref named `order`.
- If this cross-cutting identity choice lands, capture it as an ADR.
- Note: `IsBackReference` detection (see `docs/plans/csharp-export-createin-bugs-2026-08-10.md`, Finding 2) is a related but separate seam — scoping names does not fix the auto-wire bug, and vice versa.
