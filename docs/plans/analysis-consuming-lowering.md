# Plan: Wire Analysis Metadata into Lowering

**Problem:** Lowering re-discovers ~22 domain facts that analysis has already
resolved and stored as `IAnalysisMetadata`. The `INodeMetadataProvider` is
threaded through `DomainToCSharpExporter` → `BuildTypeDefsForEntity` but never
queried. `EffectLoweringPass` has no access to it at all.

**Goal:** Lowering reads from analysis metadata instead of scanning
`domain.Types.OfType<T>()`, `domain.Relationships`, and `entity.Properties`
independently. Eliminate duplicate resolution logic.

---

## Phase 0: Thread AnalysisResult into lowering context ✅

**Complete (2026-07-27).** `AnalysisResult?` threaded through `LoweringContext`,
`EffectLoweringPass`, and all internal `DomainToCSharpExporter` call chains.

**Changes:**
- `LoweringContext` carries `AnalysisResult? Analysis` (new field)
- `EffectLoweringPass` stores it, exposes via public `Analysis` property
- All 5 `LoweringContext` construction sites in `DomainToCSharpExporter` pass
  `Analysis: metadata as AnalysisResult`
- `AddActionMethod`, `LowerActionToMethodBody`, `LowerExpressionToMethodBody`
  all accept `AnalysisResult?` and thread through to context

**Verification:** Build 0W/0E, tests 1691/1691 passed.

**Risk:** None. Null-safe — falls back to current re-scan when metadata absent.
Callers with `AnalysisContext` (not `AnalysisResult`) pass null until
`EntitySyntaxPass` is updated.

---

## Phase 1: Enum type lookup (7 re-scans → 1 lookup)

**Current cost:** `domain.Types.OfType<EnumType>()` called 7 times across two
files. The fresh iterator + ToDictionary repeats identical work.

**What analysis already produces:**
- `DomainTypeLookupMetadata.Types` has all types, but requires `OfType<EnumType>`
  filtering + predicate match each time.

**Metadata to add:** Augment `DomainTypeLookupMetadata` (or create a new lightweight
`EnumTypeLookupMetadata`) with pre-built `IReadOnlyDictionary<string, EnumType>`
keyed by type name.

**Lowering changes:**

| File | Line | Before | After |
|------|------|--------|-------|
| `EffectLoweringPass.Assign` | 66 | `_domain.Types.OfType<EnumType>().ToDict(...)` | `metadata.GetEnumTypes()?.TryGetValue(...)` |
| `EffectLoweringPass.DefaultForDomainType` | 435 | Same | Same |
| `DomainToCSharpExporter.BuildTypeDefsForEntity` | 149 | Same | Same |
| `DomainToCSharpExporter.DefaultValueForProp` | 744 | Same | Same |
| `DomainToCSharpExporter.DefaultValueForTypeRef` | 938 | Same | Same |
| `DomainToCSharpExporter.BuildEnumPropertyNames` | 1202 | Same | Same |
| `DomainToCSharpExporter.MapDomainTypeRef` | 1222 | Same | Same |

**Eliminated:** 6 redundant scans. The `MapDomainTypeRef` hot path (called N×M
times per property per entity) becomes O(1) lookup.

**Analysis change:** One new metadata field on `DomainTypeLookupMetadata` or a
new record.

---

## Phase 2: Entity by name (3 re-scans → 0)

**Current:** `domain.Types.OfType<Entity>().FirstOrDefault(name)` — full iteration
each time.

**Already in:** `DomainTypeLookupMetadata.Types` is `IReadOnlyDictionary<string, DomainType>`.
No change needed to metadata shapes.

**Lowering changes:**

| File | Line | Before | After |
|------|------|--------|-------|
| `EffectLoweringPass.CreateEntityInstance` | 237 | Types.OfType<Entity>.FirstOrDefault(name) | `lookup.Types.TryGetValue(name, out Entity e)` |
| `EffectLoweringPass.CreateEntityInRelationship` | 299 | Same, via relationship.Target | `lookup.Types.TryGetValue(rel.Target.TypeName, out Entity e)` |
| `DomainToCSharpExporter.AddCreateNavMethod` | 623 | Same | `lookup.Types.TryGetValue(targetTypeName, out Entity e)` |

**Eliminated:** 3 full iteration scans. Each becomes O(1) dictionary lookup.

---

## Phase 3: Relationship by name (3 re-scans → 0)

**Current:** `domain.Relationships.FirstOrDefault(name)` — full iteration.

**Not in metadata yet.** `EffectTopologyMetadata` has `CreateInRelation` records
but no general name→relationship map.

**Metadata to add:** New `RelationshipLookupMetadata` on the `Domain` node:

```csharp
public sealed record RelationshipLookupMetadata(
    IReadOnlyDictionary<string, Relationship> Relations
) : IAnalysisMetadata;
```

Produced by a new pass or appended to `EffectTopologyPass`.

**Lowering changes:**

| File | Line | Before | After |
|------|------|--------|-------|
| `EffectLoweringPass.CreateEntityInRelationship` | 295 | `_domain.Relationships.FirstOrDefault(name)` | `metadata.Get<RelationshipLookupMetadata>(domain)?.Relations.TryGetValue(...)` |
| `DomainToCSharpExporter.CollectSubscriptionInfo` | 62 | `domainRelationships.FirstOrDefault(name)` | Same |
| `DomainToCSharpExporter.BuildTypeDefsForEntity` nav loop | 171 | Iterates `domainRelationships` list | Iterates `metadata` dict values (same, but typed) |

---

## Phase 4: Constructor parameter ordering (4 duplicates → 1 source)

**Current cost:** The "properties without DefaultValueConstraint, then singular
navs" ordering is computed independently in 4 locations:

1. `EffectLoweringPass.CreateEntityInRelationship` (lines 311-327)
2. `EffectLoweringPass.BuildConstructorArgs` (lines 398-414)
3. `DomainToCSharpExporter.AddCreateNavMethod` (lines 641-661)
4. `DomainToCSharpExporter.BuildCreateConstraintChecks` (lines 769+) — reads
   property order to emit guard clauses in matching order

**This is a correctness hazard** — if the ordering logic drifts between these
locations, generated code passes arguments in the wrong order.

**Metadata to add:** Extend `EffectiveMemberMetadata` or add a new
`ConstructorParameterOrderMetadata` per entity:

```csharp
public sealed record ConstructorParameterOrderMetadata(
    IReadOnlyList<ConstructorParameter> Parameters
) : IAnalysisMetadata;

public sealed record ConstructorParameter(
    string Name,
    DomainTypeReference Type,
    bool IsNavigation,
    bool IsBackReference  // auto-wired, not a public parameter
);
```

Produced by a new entity-scoped pass (or folded into `EntityStructureAnalyzer`).

**Lowering changes:** All 4 locations call `GetConstructorParams(entity)` from
metadata. The ordering logic lives in one place: the analysis pass.

**Eliminated:** 4× copy of the same ordering loop.

---

## Phase 5: Resolved target type for CreateEntityInRelationship (1 fix, 0 re-scans)

**Current:** `EffectLoweringPass.CreateEntityInRelationship` re-resolves
relationship → target entity → target properties.

**Already validated by:** `EffectAnalyzer.ValidateCreateEntityInRelationship`
(line 349). It already has `relationship`, `targetEntity`, and validates
every initializer property.

**Metadata to add:** Attach `ResolvedRelationshipTargetMetadata` on the effect
node during analysis:

```csharp
internal sealed record ResolvedRelationshipTargetMetadata(
    Entity TargetEntity,
    Relationship Relationship
) : IAnalysisMetadata;
```

**Lowering change:** `CreateEntityInRelationship` checks metadata first.
If present, skips the re-resolution entirely. Falls back to current logic
as safety net.

**Bonus:** This makes `ResolvedTargetType` on `CreateEntityInRelationshipEffect`
actually wired — lowering reads the resolved target from metadata and could
write it back into the effect's `InvocationResult`.

---

## Phase 6: Stage entry/exit effects (2 re-scans → 0)

**Current:** `EffectLoweringPass.StageTransition` does:
```csharp
_sourceStage = _entity.Stages.FirstOrDefault(name)  // line 109
_targetStage = _entity.Stages.FirstOrDefault(name)  // line 121
```

**Already in:** `Entity.Stages` on the domain object — this isn't metadata
re-discovery per se, but it still scans.

**Metadata to add:** Extend `EntityStructureMetadata` with a stage-name lookup
table:

```csharp
IReadOnlyDictionary<string, Stage>? StageByName  // null when HasStages is false
```

**Lowering change:** Replace `FirstOrDefault` scans with
`entityStructureMeta.StageByName.TryGetValue(...)`.

---

## Phase 7: Singular nav filtering (3 re-scans → 0)

**Current:** The "filter relationships to exclude OneToMany/ManyToMany, then
find back-reference" pattern is repeated in:

1. `EffectLoweringPass.CreateEntityInRelationship` (lines 320-327)
2. `EffectLoweringPass.BuildConstructorArgs` (lines 408)
3. `DomainToCSharpExporter.AddCreateNavMethod` (lines 632-661)

**Addressed by Phase 4** — constructor parameter ordering subsumes this.
The metadata already knows which navs are singular and which is the back-ref.

---

## Order of implementation

```
Phase 0 (thread metadata)     → unblocks everything
Phase 2 (entity by name)      → trivial, DomainTypeLookupMetadata exists
Phase 1 (enum lookup)          → small metadata change, big multiplier
Phase 3 (relationship lookup)  → small new metadata
Phase 5 (create-in target)     → wires ResolvedTargetType for real
Phase 4 (constructor order)    → largest correctness win
Phase 6 (stage lookup)         → small cleanup
Phase 7 (singular navs)        → subsumed by Phase 4
```

**Recommended starting slice:** Phase 0 + Phase 2. Low risk, no new metadata
needed, trims 3 re-scans immediately, proves the metadata→lowering pipeline
works end-to-end.

---

## Verification

- **No behavioral changes.** Lowering output must be identical before and after
  each phase (metadata reads produce same results as current scans).
- Existing tests are the regression bar — all must pass at every phase boundary.
- For Phases 1-4, add 1-2 tests per metadata type that assert the new metadata
  is produced and has the expected shape.
- No test changes in lowering — output exact-match is the contract.
