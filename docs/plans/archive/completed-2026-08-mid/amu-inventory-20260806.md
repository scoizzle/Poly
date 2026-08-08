# AMU inventory — publish × consume × residual scans (2026-08-06)

**Suite:** [`simple-agent-tasks/amu-README.md`](simple-agent-tasks/amu-README.md)
**Companion:** [`domainmodeling-cohesion-and-metadata-findings.md`](domainmodeling-cohesion-and-metadata-findings.md) §5
**Produced by:** AMU-W0 (live inventory — no production change)

---

## 1. Metadata bags published by domain analysis

All bags are `IAnalysisMetadata` records. Publisher column = the pass that calls
`context.SetMetadata`. Intermediate = published mid-pipeline on `default` node and
embedded into `DomainCatalogMetadata`; product consumers use the catalog via
`DomainSemanticLookupExtensions`.

| Bag | Publisher (file) | Node key |
|-----|------------------|----------|
| `DomainTypeLookupMetadata` (DTLM) | `SemanticDomainAnalyzer` | `default` (embedded in catalog) |
| `RelationshipLookupMetadata` (RLM) | `SemanticDomainAnalyzer` | `default` (embedded in catalog) |
| `ResolvedTypeReferenceMetadata` | `SemanticDomainAnalyzer` | each type reference node |
| `EffectivePoliciesMetadata` | `SemanticDomainAnalyzer` | entity / action / stage |
| `EffectiveMemberMetadata` | `SemanticDomainAnalyzer` | entity |
| `DomainCatalogMetadata` | `DomainCatalogPass` | domain |
| `ActionResolutionMetadata` | `DomainCatalogPass` (inside catalog) | — |
| `MutationTargetIndexMetadata` | `DomainCatalogPass` (inside catalog) | — |
| `ResolvedRelationshipTargetMetadata` | `EffectFactsPass` | each create-in effect node |
| `RequiredPropertiesMetadata` | `RequiredPropertiesPass` | entity / stage |
| `DownstreamConstraintsMetadata` | `ConstraintPropagationAnalyzer` | action parameter |
| `EntityStructureMetadata` | `EntityStructureAnalyzer` | entity |
| `EffectTopologyMetadata` | `EffectTopologyPass` | domain |
| `OwnershipAggregateMetadata` | `OwnershipAggregatePass` | domain |
| `EntityDependencyGraphMetadata` | `CrossReferencePass` | domain |
| `RelationshipContractMetadata` | `RuntimeContractAnalyzer` | `default` |
| `SubscriptionDispatchPlanMetadata` | `RuntimeContractAnalyzer` | stage / entity |
| `ActionCapabilityMetadata` | `CapabilityAnalyzer` | action |
| `StageCapabilityMetadata` | `CapabilityAnalyzer` | stage |
| `RelationshipCapabilityMetadata` | `CapabilityAnalyzer` | relationship |
| `BehaviorMetadata` | `BehaviorPass` | domain |
| `StorageMappingMetadata` | `StoragePass` | domain |
| `TransportMetadata` | `TransportPass` | domain |

Lint-only passes that publish **no** bags: `EffectAnalyzer`, `PolicyConstraintAnalyzer`,
`SubscriptionAnalyzer`, `ConstraintQualityAnalyzer`, `RuleCoverageAnalyzer`,
`AuthoringSuggestionAnalyzer`, `ContractIntegrationAnalyzer`.

## 2. Consumers per bag

| Bag | Consumers |
|-----|-----------|
| DTLM / RLM (via catalog) | `DomainCatalogPass`, `EntityStructureAnalyzer`, `EffectFactsPass`, `StorageAnalyzer`, `BehaviorPass`, `CapabilityAnalyzer`, `EffectAnalyzer`, `PolicyConstraintAnalyzer`, `SubscriptionAnalyzer`, `ConstraintQualityAnalyzer`, `AuthoringSuggestionAnalyzer`, `RuntimeContractAnalyzer`, `DomainQueries` |
| `ResolvedTypeReferenceMetadata` | `EffectAnalyzer`, `SemanticDomainAnalyzer`, `BehaviorPass`, `ConstraintQualityAnalyzer` |
| `EffectiveMemberMetadata` | `DomainQueries.GetEntity` |
| `StageCapabilityMetadata` | `DomainEffectiveSurface` (helpers), `DomainQueries.GetEntity`, `SubscriptionAnalyzer` |
| `DomainCatalogMetadata` | `DomainSemanticLookupExtensions` (product lookups), `DomainInstanceStore`, `CapabilityAnalyzer` |
| `ResolvedRelationshipTargetMetadata` | `EffectLoweringPass` (create-in) |
| `RequiredPropertiesMetadata` | `EffectAnalyzer`, `RuleCoverageAnalyzer` |
| `DownstreamConstraintsMetadata` | `EffectAnalyzer` |
| `EntityStructureMetadata` | `DomainSemanticLookupExtensions.TryGetStage`, `OwnershipAggregatePass`, `StorageAnalyzer`, `DomainToCSharpExporter`, `EffectLoweringPass`, `DomainEntityInstance`, `DomainInstanceStore`, MCP `get_domain_analysis` (roots) |
| `EffectTopologyMetadata` | `CrossReferencePass`, `OwnershipAggregatePass`, `StoragePass`, `TransportPass`, MCP `get_domain_analysis` |
| `OwnershipAggregateMetadata` | `StoragePass`, `TransportPass` |
| `RelationshipContractMetadata` | `RuntimeContractAnalyzer`, `DomainInstanceStore`, `DomainSemanticLookupExtensions` |
| `SubscriptionDispatchPlanMetadata` | `DomainInstanceStore.NotifyTransition` |
| `BehaviorMetadata` | MCP `get_domain_analysis` (action names), codegen packs |
| `StorageMappingMetadata` / `TransportMetadata` | codegen packs; MCP flags only |

## 3. Residual scans (high-signal, grep-verified 2026-08-06)

> Legend: **EA** = EffectAnalyzer, **PCA** = PolicyConstraintAnalyzer, **SA** = SubscriptionAnalyzer,
> **RCA** = RuntimeContractAnalyzer, **StA** = StorageAnalyzer/StoragePass, **Exp** = DomainToCSharpExporter,
> **EL** = EffectLoweringPass, **PP** = DomainProgramProjection (pack-side), **DI** = DomainEntityInstance.

### 3.1 Analysis passes (W1 / W2)

| Row | Site | Pattern | Owned by |
|-----|------|---------|----------|
| R01 | EA `ValidateCreateWithRelationshipName` | `domain.Relationships.FirstOrDefault(r => r.Name == cei.RelationshipName)` | W1.1 |
| R02 | EA `ValidateCreateEntityInRelationship` | `domain.Relationships.FirstOrDefault(r => r.Name == createIn.RelationshipName)` | W1.1 |
| R03 | EA `ValidateInvokeAction` (hasRel branch) | `domain.Relationships.FirstOrDefault(r => r.Name == iae.TargetRelationship)` + `Types.OfType<Entity>().FirstOrDefault(e => e.Name == targetTypeName)` | W1.1 |
| R04 | EA `ValidateRelationshipName` | `domain.Relationships.Any(r => r.Name == relationshipName)` | W1.1 |
| R05 | EA `ValidateTransitionRelationship` | `domain.Relationships.FirstOrDefault(r => r.Name == tre.RelationshipName)` + `relationship.Stages.Any` | W1.1 |
| R06 | EA `IsExclusivelyOwned` | full `foreach (var rel in domain.Relationships)` scan | W1.1 (deliberate full walk — catalog lookup reuse OK if helper exists; otherwise keep documented) |
| R07 | PCA `ValidateQuantifierRelationship` | `domain.Relationships.FirstOrDefault(r => r.Name == resolvedRelName)` | W1.2 |
| R08 | PCA `ValidateRelationshipCardinality` | `domain.Relationships.FirstOrDefault(...)` ×2 (source-keyed + reverse-name lookup) | W1.2 |
| R09 | PCA `IsRelationshipOnEntity` (referenced at line ~107) | per-property rel check (verify impl) | W1.2 |
| R10 | SA `ValidateDomain` causality edge build | `domain.Relationships.FirstOrDefault(r => r.Source == entity && r.Name == sub.RelationshipName)` | W1.3 |
| R11 | SA `ValidateSubscription` | `domain.Relationships.FirstOrDefault(r => r.Name == subscription.RelationshipName && r.Source == entity)` | W1.3 |
| R12 | StA `StorageAnalyzer` fallback branch | `_domain.Types.OfType<Entity>()` when DTLM absent (already prefers lookup — ok) | W2.1 (verify) |
| R13 | StA `BuildStorageEntity` meta-absent branch | `entity.Properties.FirstOrDefault(UniqueConstraint)` + `Types.OfType<EnumType>().FirstOrDefault(e.Name == "{entity}Stage")` re-derivation | W2.1 |
| R14 | StA `ClassifyProperties` | `_domain.Types.OfType<EnumType>().ToDictionary(...)` per-call rebuild | W2.1 |

### 3.2 Lowering / export (W3)

| Row | Site | Pattern | Owned by |
|-----|------|---------|----------|
| R15 | Exp `BuildEntities` (~158) | `domain.Types.OfType<EnumType>()` when analysis null | W3.1 (analysis-present path must use lookup; keep domain iteration for emission order) |
| R16 | Exp action return-type block (~768) | `domain.Types.OfType<EnumType>()` | W3.1 |
| R17 | Exp `ResolveTypeName` / return-type (~962, ~1241) | `domain.Types.OfType<EnumType>()` | W3.1 |
| R18 | Exp relationship resolution (~1300) | `domainRelationships.FirstOrDefault(r => ...)` | W3.1 |
| R19 | Exp (~1311) | `domain.Types.OfType<EnumType>()` | W3.1 |
| R20 | EL `ResolveCreateInTarget` (~498) | `_domain.Types.OfType<Entity>().FirstOrDefault(e => e.Name == ...)` | W3.2 |
| R21 | EL relationship resolve (~516) | `_domain.Relationships.FirstOrDefault(r => ...)` | W3.2 |
| R22 | EL create-in/transition rel walk (~445) | `_domain.Relationships.Where(r => r.Source == ...)` | W3.2 (verify analysis-present branch) |

### 3.3 Pack / runtime (inventory only — no task)

| Row | Site | Pattern |
|-----|------|---------|
| R23 | PP `DomainProgramProjection` (pack) | `domain.Types.OfType<Entity>()` + `OfType<EnumType>()` — pack-side, out of suite scope |
| R24 | DI `DomainEntityInstance` (~283) | ESM read (already metadata) — runtime IR-first by design |

### 3.4 Post-review verification (2026-08-06, review F4)

Phenomenal review (Issue 4) required: **analysis-present branches must never hit
`FirstOrDefault` / `OfType` tree scans**; scans survive only as explicit
analysis-absent residuals. Re-verified 2026-08-06 after F4:

- R12/R13 (StorageAnalyzer): analysis-present paths use DTLM/EntityStructure;
  tree scans are in the lookup-null branch only. R14 (`ClassifyProperties`
  enum map) was the last always-scan — **fixed**: enum names now come from the
  DTLM when analysis is present (`_enumTypeNames`), tree scan is
  analysis-absent residual only.
- R15–R19 (DomainToCSharpExporter): `TryResolveEnumType`, `BuildEnumPropertyNames`,
  `ResolveRelationship`, `MapDomainTypeRef` are catalog-first and **fail closed**
  (throw or return null) when analysis is present — no tree rescan. Null-analysis
  `OfType<EnumType>` / `FirstOrDefault` remain under the documented
  standalone/reduced-contract path.
- R20–R22 (EffectLoweringPass): `ResolveEntity` / `ResolveRelationship` are
  catalog-first via `GetTypeLookup` / `GetRelationshipLookup` and fail closed
  (return null) when analysis is present; constructor order uses
  `EntityStructureMetadata` (throws when absent) — tree scans are
  analysis-absent only.
- R06 (`IsExclusivelyOwned` full walk): accepted deliberate full walk
  (documented; no catalog reuse helper).

**Contract:** with `DomainModelAnalyzer.Analyze` (or any pipeline that includes
Semantic + DomainCatalogPass), no product path tree-scans for name resolution.

## 4. Task → residual ownership map

| Task | Owns rows |
|------|-----------|
| W1.1 EffectAnalyzer | R01–R06 |
| W1.2 PolicyConstraintAnalyzer | R07–R09 |
| W1.3 Subscription/RuntimeContract | R10–R11 |
| W2.1 Dependencies + Storage←EntityStructure | R12–R14 + `Dependencies` audit (empty arrays: Semantic, Structural, EffectTopology, ConstraintPropagation, ContractIntegration) |
| W3.1 Exporter | R15–R19 |
| W3.2 EffectLowering | R20–R22 |
| W4 MCP facts | no residual row — adds projection over existing bags (roots/aggregate/subscription signal) |
