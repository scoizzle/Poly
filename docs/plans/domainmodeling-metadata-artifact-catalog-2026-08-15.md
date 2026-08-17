# DomainModeling — metadata and artifact catalog

**Date:** 2026-08-15  
**Scope:** product DomainModeling + compiler/MCP emit. Not Interpretation VM analysis bags.  
**Status:** Identification. Not CURRENT.

**Rule this catalog assumes:** one bag answers one question. Later bags compose; they do not remake the catalog.

---

## 1. How facts move

```text
.poly  →  Domain (facts + uses ids)
       →  DomainSession (libraries loaded: Language, folds, meaning, type maps, artifacts)
       →  DomainModelAnalyzer (fixed pipeline)
       →  IAnalysisMetadata on nodes
       →  consume: runtime | MCP | lower/export | IArtifactContributor
```

- **Write:** `context.SetMetadata(node, bag)` in an `INodeAnalyzer`.
- **Read:** `analysis.GetMetadata<T>(node)` or `DomainSemanticLookupExtensions` (catalog / capability / structure).
- **Library register:** `IDomainLibrary.Register(DomainHostBuilder)` may add DSL, analysis inputs, and artifact contributors. Not MEF.

Session-level tables (not analysis metadata): `AnnotationRegistry`, `ExpressionFormRegistry` / `ExpressionFoldTable`, `ExpressionMeaning`, `TypeMappingRegistry`, `IStorageConvention[]`, `IArtifactContributor[]`.

---

## 2. Analysis pipeline (order)

| # | Pass | Publishes | Kind |
|---|------|-----------|------|
| 1 | `StructuralDomainAnalyzer` | — | Well-formedness (duplicates, reserved names). Fail closed. |
| 2 | `DomainCatalogPass` | Catalog + aliases + owners + resolved type refs | **First metadata** |
| 3 | `RuntimeContractAnalyzer` | Relationship contracts + subscription dispatch plans | Derived runtime |
| 4 | `RequiredPropertiesPass` | Required-by-policy / `required` | Derived |
| 5 | `PolicyConstraintAnalyzer` | — | Fail-closed policy diagnostics |
| 6 | `ExpressionTypeAnalyzer` | — | Fail-closed expression types |
| 7 | `ConstraintPropagationAnalyzer` | Downstream constraints on action params | Derived |
| 8 | `EffectFactsPass` | Resolved create-in target | Derived |
| 9 | `EffectInvariantAnalyzer` | Action stage invariants | Derived |
| 10 | `EffectAnalyzer` | — | Fail-closed effect diagnostics |
| 11 | `ConstraintQualityAnalyzer` | — | Satisfiability lint |
| 12 | `CapabilityAnalyzer` | Effective action/stage views | Derived (canonical effective surface) |
| 13 | `RuleCoverageAnalyzer` | — | Hint |
| 14 | `ContractIntegrationAnalyzer` | — | Fail-closed contract clash |
| 15 | `EntityStructureAnalyzer` | Per-entity keys / ctor / IsRoot / stages | Derived + **copy of catalog stages** |
| 16 | `SubscriptionAnalyzer` | — | Fail-closed `when` |
| 17 | `EffectTopologyPass` | Cross-entity topology | Derived |
| 18 | `OwnershipAggregatePass` | Aggregate tree | Derived (copies IsRoot) |
| 19 | `CrossReferencePass` | — | Cycle **warning**; rebuilds a private rel index |
| 20 | `StoragePass` | Storage mapping | Derived emit |
| 21 | `AuthoringSuggestionAnalyzer` | — | Hint |

`DomainModelAnalyzer.Analyze` is a **cached** pipeline. It does not take session `AdditionalPasses`.

---

## 3. Metadata bags

### 3.1 Catalog (one name index)

| Bag | Hung on | Writer | Question | Product readers |
|-----|---------|--------|----------|-----------------|
| `DomainCatalogMetadata` | Domain | Catalog pass | Types, navs, actions, stages, mutation index | Lookups, evolution, runtime catalog gate, MCP describe |
| `DomainTypeLookupMetadata` | `default` | Catalog pass (**same instance** as `catalog.Types`) | Name → type (child-node walk) | Mid-pipeline via `GetTypeLookup()` |
| `RelationshipLookupMetadata` | `default` | Catalog pass (**same instance** as `catalog.Relationships`) | (source, nav) → relationship | Mid-pipeline via `GetRelationshipLookup()` |
| `ActionResolutionMetadata` | *inside catalog only* | Catalog pass | Entity/stage action maps | `TryResolveAction` |
| `MutationTargetIndexMetadata` | *inside catalog only* | Catalog pass | Name maps for evolution apply | `DomainEvolution` / `DomainMutationContext` |
| `OwnerEntityMetadata` | Action, Stage | Catalog pass | Which entity owns this node | Capability, RuntimeContract |
| `ResolvedTypeReferenceMetadata` | `DomainTypeReference` | Catalog pass | This type ref → `DomainType` | Effect, ConstraintQuality, Behavior projection |

`ActionResolutionMetadata` / `MutationTargetIndexMetadata` must **not** be hung on entity/domain as separate bags (tests assert they are null there).

**Overlap:** MTI types/rels/stages/actions remake catalog maps. ESM `StageByName` remakes catalog stages. CrossReference rebuilds rels from `domain.Relationships`.

### 3.2 Derived (new question)

| Bag | Hung on | Writer | Question | Product readers |
|-----|---------|--------|----------|-----------------|
| `RelationshipContractMetadata` | `default` | RuntimeContract | Rel shape for dispatch | Runtime store, MCP, inbound/outbound helpers |
| `SubscriptionDispatchPlanMetadata` | Entity, Stage | RuntimeContract | How `when` fires | Runtime store, C# export, MCP analysis |
| `RequiredPropertiesMetadata` | Entity, Stage | RequiredProperties | What must be set | EffectAnalyzer, RuleCoverage |
| `DownstreamConstraintsMetadata` | Action param | ConstraintPropagation | Constraints flowing into create-in | EffectAnalyzer |
| `ResolvedRelationshipTargetMetadata` | Create-in effect | EffectFacts | Which rel/entity this create-in hits | EffectLoweringPass, EffectAnalyzer |
| `ActionInvariantMetadata` | Action | EffectInvariant | Stage-context invariants | EffectAnalyzer, StorageAnalyzer |
| `ActionCapabilityMetadata` | Action | Capability | Effective policies, transitions | Behavior projection, Oracle describe, SubscriptionAnalyzer |
| `StageCapabilityMetadata` | Stage | Capability | Effective policies/actions | `GetEffectivePolicies` / `GetEffectiveActions`, DomainQueries |
| `EntityStructureMetadata` | Entity | EntityStructure | IsRoot, key, ctor, enum props, **StageByName** | Export, storage, MCP roots, some runtime (structure only) |
| `EffectTopologyMetadata` | Domain | EffectTopology | Create-in / invoke / subscription graph | Ownership, Storage, CrossReference, MCP |
| `OwnershipAggregateMetadata` | Domain | Ownership | Parent/child aggregates | Storage, compiler, Minimal API, MCP |
| `StorageMappingMetadata` | Domain | StoragePass | Tables/columns/FKs | Compiler, DbContext, Minimal API, MCP |

`BehaviorMetadata` is **not** a pipeline bag. `BehaviorMetadata.From(domain, analysis)` projects capability at emit/read time.

### 3.3 Diagnostic-only (no bag)

Structural, PolicyConstraint, ExpressionType, Effect, ConstraintQuality, RuleCoverage, ContractIntegration, Subscription, CrossReference, AuthoringSuggestion.

Several of these **fail closed** (Structural, Policy, ExpressionType, Effect, Contract, Subscription). Calling them all “lint” is the lie. Hints only: RuleCoverage, AuthoringSuggestion, CrossReference (warning).

---

## 4. Artifact production

Three **kinds** of output. Only the third is the library hook.

### 4.1 Always-on core emit (not a library)

| Output | Implementation | Inputs |
|--------|----------------|--------|
| `{Entity}.cs`, `Poly.Types.cs` | `DomainProgramProjection.ToSyntax` → `DomainToCSharpExporter` → `CSharpGenerator` | Domain + analysis (catalog, structure, dispatch plans, …) |
| `.poly` text | `DomainDslPrinter` | Session language + Domain |
| MCP C# export | same exporter as compiler entities | Domain + analysis |

`DomainProgramProjection` still **delegates** to `DomainToCSharpExporter` (unfinished move).

### 4.2 Compiler-mode emit (CLI policy, not `uses`)

| Output | When | Implementation |
|--------|------|----------------|
| `{Domain}DbContext.cs` | `CompileMode.Db` / `All` | `DbContextGenerator` + storage bag |
| `Program.cs` + `demo.http` | `CompileMode.All` | `MinimalApiHostArtifactContributor` registered on the **same** artifact list as libraries |

Minimal API contributor is lazy: it reads storage / capability-projected behavior / aggregate from analysis. It is not a second plugin type.

### 4.3 Library artifacts (the extension hook)

| Piece | Role |
|-------|------|
| `IArtifactContributor.Contribute(domain, analysis)` | One method; files or empty |
| `DomainHostBuilder.AddArtifactContributor` | Libraries call this in `Register` |
| `DomainHost.Artifacts` / `DomainSession.Artifacts` | Frozen list after load |
| `DslCompiler` | After analysis succeeds, asks that list. Structural failure → no contribute |
| `DslCompiler.AddArtifactContributor` / `Load` | One-off library on the compiler instance |

No product library except the compiler’s All-mode host contributor ships files today. Temporal / storage / sqlite register DSL or type maps only.

### 4.4 What libraries can extend today vs claimed

| Claim | Reality |
|-------|---------|
| DSL | **Yes** — grammar, folds, print, meaning, annotations |
| Analysis **inputs** | **Partial** — type maps and storage conventions reach `StorageAnalyzer`. |
| Analysis **passes** | **Removed** — `AddAnalysisPass` / `AdditionalPasses` deleted (never ran). |
| Artifacts | **Yes** — contributor list on the session, asked after analysis |

---

## 5. Architecture opportunities

Ordered by honesty of the extension story, then leftover duals.

### A. Analysis extension

`AddAnalysisPass` deleted (2026-08-15). Libraries register concepts (meaning, type maps, artifacts), not extra pipeline types, until `session.Analyze` exists.

### B. One emit path for files

Today: core types inline in `GenerateAllFiles`, DbContext inline, host files via contributor. Opportunity: DbContext (and eventually entity files) as contributors too, so “what files come out” is one loop. Entity emit staying core is fine if we **name** it as the non-library path. The half-state is the problem.

`DomainProgramProjection` should either own the walk or die as a façade.

### C. Stop remaking the catalog

- Drop `EntityStructureMetadata.StageByName` (catalog already has stages; `TryGetStage` uses it).
- Treat `MutationTargetIndex` as evolution’s view **or** fold apply-time lookup into catalog Types/Actions — do not keep a parallel public name map.
- `CrossReferencePass` should read `GetRelationshipLookup(domain)`, not `domain.Relationships`.
- Default DTLM/RLM aliases are the same instances — keep until child-node passes always have a Domain; then hang lookups only on the Domain.

### D. Hint-pass collapse

`AuthoringSuggestion` + `RuleCoverage` (+ CrossReference if the warning can live on topology) do not earn three types. Merge or drop. Do **not** merge Policy/Effect/Subscription — those fail closed.

### E. Storage is one type

`StoragePass` wraps `StorageAnalyzer`. One type. `IsRoot` already copies structure; do not recompute.

### F. Names still teach the old story

`DomainHost` / `DomainHostBuilder` / `Packs/` / `DbmsPack` are the session assembler and vendor seed. Rename when touching that layer. The **system** is already `IDomainLibrary`.

### G. Do not do

- A second plugin interface beside `IDomainLibrary`
- Stuffing capability / storage / dispatch into the catalog
- Splitting the exporter or `DomainEntityInstance` for file size
- Rewriting `PolyDslParser` for this catalog

---

## 6. Target shape

```text
Library.Register(session builder)
        ├─ DSL tables
        ├─ analysis inputs (+ extra passes if Analyze is session-scoped)
        └─ artifact contributors

Analyze(domain, session) = product pipeline + session extra passes
Emit                   = core types (named) + session.Artifacts
```

A newcomer adding `uses Foo` reads: Domain, Session, Catalog pass, `IDomainLibrary`, one Foo file.

---

## Related

- [`domainmodeling-cleanup-inventory-2026-08-15.md`](domainmodeling-cleanup-inventory-2026-08-15.md)
- [`../decisions/2026-08-14-domain-libraries.md`](../decisions/2026-08-14-domain-libraries.md)
- [`../CORE.md`](../CORE.md) § Domain catalog / effective surface
