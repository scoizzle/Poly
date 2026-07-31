# Domain analysis inventory and simplification plan

**Date:** 2026-07-30  
**Status:** Open — present-state inventory + tactical cutovers (not executed)  
**Future target (read first for direction):** [`domain-analysis-future-state.md`](./domain-analysis-future-state.md)  
**Related:** [`domain-analysis-unification.md`](./domain-analysis-unification.md) · [`docs/CORE.md`](../CORE.md) · DACR suite (`simple-agent-tasks/dacr-*`)  

## 1. Problem statement

Domain analysis has accreted **~20 pipeline passes**, overlapping indexes, mixed “walk the tree and diagnose” with “publish lookup maps,” and at least one pass that is **not analysis at all** (`EntitySyntaxPass` — full Syntax IR projection). Dependencies are incomplete or empty for most passes, so ordering is registration-order luck. Consumers cast `INodeMetadataProvider` to `AnalysisResult` and miss metadata that already exists on `AnalysisContext`. Failures are often soft (null metadata, warnings, residual tree scans tagged `DM-META-REMOVE-FALLBACK`).

**Symptom the user hit:** EntitySyntaxPass “doesn’t work” — see §4.

This document inventories **today’s** passes and maps cutovers (S0–S4) onto the **future-state** waves (W0–W4) in [`domain-analysis-future-state.md`](./domain-analysis-future-state.md). Prefer that doc for architecture acceptance criteria.

---

## 2. Pipeline order (today)

Registered in `DomainModelAnalyzer.UseDomainModelAnalysisPipeline()`:

| # | Pass | Declared deps | Walk style |
|---|---|---|---|
| 1 | `StructuralDomainAnalyzer` | none | Recursive node walk |
| 2 | `SemanticDomainAnalyzer` | none | Recursive node walk |
| 3 | `RuntimeContractAnalyzer` | Semantic | Recursive (Domain / Entity / Stage cases) |
| 4 | `PolicyConstraintAnalyzer` | none | Recursive / entity walks |
| 5 | `EffectAnalyzer` | none | Recursive effect walks |
| 6 | `ConstraintQualityAnalyzer` | none | Diagnostics |
| 7 | `CapabilityAnalyzer` | none | Domain-level entity walk + children |
| 8 | `ConstraintPropagationAnalyzer` | none | Parameter constraints |
| 9 | `RuleCoverageAnalyzer` | none | Diagnostics |
| 10 | `ContractIntegrationAnalyzer` | none | Diagnostics |
| 11 | `EntityStructureAnalyzer` | Semantic | Domain once |
| 12 | `SubscriptionAnalyzer` | Capability | Diagnostics + contracts |
| 13 | `EffectTopologyPass` | none | Domain once |
| 14 | `OwnershipAggregatePass` | Topology, EntityStructure | Domain once |
| 15 | `BehaviorPass` | Semantic, Capability | Domain once |
| 16 | `CrossReferencePass` | Topology | Domain once |
| 17 | `StoragePass` | Topology, Ownership | Domain once (+ pack ctor) |
| 18 | `TransportPass` | Topology, Ownership | Domain once |
| 19 | `AuthoringSuggestionAnalyzer` | none | Diagnostics |
| 20 | `EntitySyntaxPass` | none | Domain once → **projection** |

Incremental analysis wraps the same builder (`UseIncrementalAnalysis`).

---

## 3. Inventory: discovery product per pass

### 3.1 Tier A — Indexes (answer “where is X?”)

| Pass | Metadata (key) | Information discovered | Primary consumers |
|---|---|---|---|
| **SemanticDomainAnalyzer** | `DomainTypeLookupMetadata` (`default`) | Name → `DomainType`; entity set | Almost everything; lookups; ESM; Behavior; lowering type resolve |
| | `RelationshipLookupMetadata` (`default`) | Name → `Relationship` | Runtime/MCP/export relationship resolve |
| | `ResolvedTypeReferenceMetadata` (type ref nodes) | Property/result type → resolved `DomainType` | Semantics validation; entity-ref param detection |
| | `EffectivePoliciesMetadata` (entity / stage / action) | Policy inheritance composition | Behavior; queries; capability-adjacent |
| | `EffectiveMemberMetadata` (entity) | Effective props/actions/policies/stages | Export expectations; queries |
| **RuntimeContractAnalyzer** | `RelationshipContractMetadata` (`default`) | Flat contract list (name, ends, cardinality, owns) | `DomainInstanceStore.NotifyTransition` |
| | `ActionResolutionMetadata` (entity) | Entity + per-stage action maps | Runtime `TryResolveAction`; MCP DescribeAction |
| | `MutationTargetIndexMetadata` (domain) | Types, entities, relationships, stages, actions, **all policy maps** | Evolution mutations; MCP DescribePolicy; `GetEffectivePolicies` |
| | `SubscriptionDispatchPlanMetadata` (stage) | Relationship → subscription dispatch entries | `NotifyTransition` |

**Overlap (critical):** MTI re-indexes types/entities/relationships/stages/actions/policies already partly in Semantic + ARM. Three places answer “actions on entity E.”

### 3.2 Tier B — Structural / semantic validation (diagnostics-first)

| Pass | Metadata | Information discovered | Notes |
|---|---|---|---|
| **StructuralDomainAnalyzer** | (mostly diagnostics) | Duplicate names; ownership cardinality shape | Pure shape of the tree |
| **SemanticDomainAnalyzer** | (above + diagnostics) | Unknown types; primitive category rules; relationship end validity; create-entity checks | Mixed **index + validate** in one pass |
| **PolicyConstraintAnalyzer** | `RequiredPropertiesMetadata` (entity/stage) | Properties required by policies | Large diagnostic surface (~470 LOC) |
| **EffectAnalyzer** | `ResolvedRelationshipTargetMetadata` (create-in effect nodes) | create-in → relationship + target entity | Huge diagnostic surface (~990 LOC); only one small metadata product |
| **ConstraintQualityAnalyzer** | diagnostics | Constraint quality hints | No durable bag |
| **RuleCoverageAnalyzer** | diagnostics | Rule coverage | No durable bag |
| **ContractIntegrationAnalyzer** | diagnostics | Contract integration | No durable bag |
| **SubscriptionAnalyzer** | diagnostics | Subscription contract / causality / replay | Depends on Capability; mostly diagnostics |
| **AuthoringSuggestionAnalyzer** | diagnostics | Authoring suggestions | Advisory only |

### 3.3 Tier C — Derived domain facts (views for packs / runtime)

| Pass | Metadata | Information discovered | Primary consumers |
|---|---|---|---|
| **CapabilityAnalyzer** | `ActionCapabilityMetadata` / `StageCapabilityMetadata` / `RelationshipCapabilityMetadata` | Local vs effective actions/policies; transition targets (partial — stub Stage nodes) | MCP DescribeStage; BehaviorPass; SubscriptionAnalyzer |
| **EntityStructureAnalyzer** | `EntityStructureMetadata` (entity) | Root?, key, soft-delete, stages, **StageByName**, constructor param order | Runtime notify; lowering create; export ctor order |
| **EffectTopologyPass** | `EffectTopologyMetadata` (domain) | Create-in edges, cross-invokes, subscription edges | Ownership; Storage; Transport; CrossReference |
| **OwnershipAggregatePass** | `OwnershipAggregateMetadata` (domain) | Roots, children, aggregate parents | Storage; Transport; DslCompiler |
| **BehaviorPass** | `BehaviorMetadata` (domain) | Per-action params, policies, transitions (codegen-oriented model) | DslCompiler / packs |
| **CrossReferencePass** | `EntityDependencyGraphMetadata` (domain) | Dependency edges + cycles | Diagnostics; future pack use |
| **StoragePass** | `StorageMappingMetadata` (domain) | Columns, FKs, navs, tables | DslCompiler DB mode; packs |
| **TransportPass** | `TransportMetadata` (domain) | Exposable API surface / nesting | Packs / MinimalApi |
| **ConstraintPropagationAnalyzer** | `DownstreamConstraintsMetadata` (params) | Constraints flowing to parameters | Narrow consumers |

### 3.4 Tier D — Projection (should not be analysis)

| Pass | Metadata | Information discovered | Primary consumers |
|---|---|---|---|
| **EntitySyntaxPass** | `EntitySyntaxMetadata` (domain) | Full `TypeDefinitionNode[]` (entities, stage enums, DomainResult IR) | **DslCompiler entity emit only** |

This is **codegen**, not discovery. It re-enters `DomainProgramProjection` / `DomainToCSharpExporter` during analysis.

### 3.5 Helpers (not pipeline passes)

| Type | Role |
|---|---|
| `DomainSemanticLookupExtensions` | Consumer-facing lookup API over MTI / ARM / ESM / RLM |
| `RuntimeAnalysisCache` | Domain → `AnalysisResult` for runtime |
| `DomainAnalysis` | Iteration helpers |
| `StorageAnalyzer` | Algorithm behind StoragePass (still Lowering-adjacent home historically) |

---

## 4. Why EntitySyntaxPass fails (root causes)

### 4.1 Wrong abstraction boundary

```text
EntitySyntaxPass.Analyze
  → DomainProgramProjection.ToSyntax(domain, context)   // AnalysisContext as INodeMetadataProvider
    → DomainToCSharpExporter.BuildTypeDefsForEntity(...)
         var analysis = metadata as AnalysisResult;     // ALWAYS NULL for AnalysisContext
```

Almost every semantic lookup in the exporter does `metadata as AnalysisResult` then `analysis?.GetMetadata<…>`. During the pass, **all analysis metadata is invisible** even though it sits on the same `AnalysisContext`. Projection falls back to tree scans / incomplete paths.

### 4.2 Soft-fail design

```csharp
catch (Exception ex) {
    context.ReportDiagnostic(..., Warning, $"Entity syntax projection failed: {ex.Message}");
}
```

Any throw (including F5-style fail-closed throws if wiring ever passes a real `AnalysisResult`, or any exporter bug) → **warning only**, no `EntitySyntaxMetadata`. DslCompiler:

```csharp
var entitySyntax = analysis.GetMetadata<EntitySyntaxMetadata>(domain);
if (entitySyntax is not null) { /* emit */ }
// else: silent skip of all entity files
```

So “not working” looks like **empty codegen**, not a hard analyze failure.

### 4.3 Circular responsibility

Analysis is supposed to produce **facts**. EntitySyntaxPass produces **host IR** that depends on those facts *and* on exporter policy. Mid-pipeline projection:

- Couples Analysis → Lowering/export (hard dependency reverse of CORE “Lowering depends on Analysis”).
- Forces dual-path exporter (`AnalysisResult?` nullable) forever.
- Makes DACR fail-closed rules fight the pass (nullable analysis “for EntitySyntaxPass path”).

### 4.4 No declared dependencies

`Dependencies => []` while it needs Semantic indexes, RLM, ESM, etc. Order is only “last in list.” Incremental analysis invalidation is underspecified.

### 4.5 What “fixed” means (for a later PR)

1. **Remove EntitySyntaxPass from the core pipeline**, or  
2. If kept temporarily: pass a proper metadata adapter that implements `GetMetadata` on `AnalysisContext` without casting to `AnalysisResult`, **and** fail closed (Error diagnostic + no silent skip), **and** declare deps.  
3. Prefer: **DslCompiler / export entrypoints** call `DomainProgramProjection.ToSyntax(domain, analysis)` once on the **finished** `AnalysisResult`.

---

## 5. Incoherence themes (cross-cutting)

| Theme | Evidence | Cost |
|---|---|---|
| **Multiple indexes of the same graph** | Semantic DTLM/RLM + MTI + ARM + ESM.StageByName | Key mismatches (DACR class of bugs); triple maintenance |
| **Multiple “effective policy/action” models** | EffectivePoliciesMetadata, StageCapabilityView, BehaviorAction, GetEffectivePolicies(MTI) | MCP/runtime/export disagree |
| **Diagnostics-only mega-passes** | EffectAnalyzer ~1k LOC, PolicyConstraint ~470 LOC | Hard to test; obscure what is “fact” vs “lint” |
| **Empty Dependencies** | Most passes | False confidence; incremental analysis risk |
| **Thin wrapper / dual home** | StoragePass → StorageAnalyzer; topology historically Lowering | DAU incomplete |
| **Projection inside analyze** | EntitySyntaxPass | §4 |
| **Residual scans** | `DM-META-REMOVE-FALLBACK` (~34 sites) | Dual paths forever until AnalysisResult is universal |
| **Capability transition stubs** | `new Stage(name, [], …)` without real stage nodes | Incomplete capability views |

---

## 6. Target architecture (simplified)

```text
                    ┌─────────────────────────────┐
                    │  Domain (immutable tree)    │
                    └─────────────┬───────────────┘
                                  │
         ┌────────────────────────┼────────────────────────┐
         ▼                        ▼                        ▼
   Validate (lint)          Index (lookups)          Derive (views)
   structure/semantic       ONE fact index           topology, ownership,
   policies/effects         types/rels/actions       capability/behavior,
   subscriptions            stages/policies          storage/transport
         │                        │                        │
         └────────────────────────┼────────────────────────┘
                                  ▼
                         AnalysisResult
                    (facts + diagnostics only)
                                  │
              ┌───────────────────┼───────────────────┐
              ▼                   ▼                   ▼
         Runtime            MCP / queries         Export / packs
    (dispatch maps)       (describe/oracle)     DomainProgramProjection
                                                → Syntax IR (NOT a pass)
```

### Single index rule

**One** domain-keyed (or default-keyed) catalog owns:

- types / entities  
- relationships  
- stages by entity  
- actions by entity (entity-level + stage-scoped)  
- policies by entity / stage / action  

Today that is closest to **`MutationTargetIndexMetadata` + Semantic DTLM/RLM**. Target: **merge Semantic index + RuntimeContract maps into one `DomainCatalogMetadata`** (name TBD), with thin views:

- Runtime: action resolution + subscription plans (can remain stage-keyed bags built from catalog)  
- Evolution: same catalog  
- MCP: same catalog  

### Separation rule

| Kind | Lives in Analysis? | Output |
|---|---|---|
| Validation diagnostics | Yes | Diagnostics only |
| Lookup / structure facts | Yes | Metadata bags |
| Pack-refined storage/transport | Yes (core defaults) + pack overlays | Metadata bags |
| Host Syntax IR / C# text | **No** | Export pipeline only |

---

## 7. Simplification plan (phased)

Phases below are tactical; each is a wave in the future-state plan: **S0→W0**, **S1→W1**, **S2→W2**, **S3→W3**, **S4→W4**.

### Phase S0 — Stabilize EntitySyntax (unblock codegen) — **Small / first** (= W0)

**Goal:** Entity files emit again; analysis no longer pretends projection is a fact.

1. Remove `EntitySyntaxPass` from `UseDomainModelAnalysisPipeline` **or** gate it off by default.  
2. Change `DslCompiler.GenerateAllFiles` to:

   ```csharp
   var types = DomainProgramProjection.ToSyntax(domain, analysis);
   // emit from types
   ```

3. Delete soft-catch path that hides projection failures; export fails loud.  
4. Fix exporter to use `INodeMetadataProvider` **without** `as AnalysisResult` for GetMetadata (if any mid-pass remains).  
5. Update `PipelineMergeMetadataTests` (EntitySyntaxMetadata assertion → export-path test).  
6. Document in CORE: “Syntax IR is export, not analysis.”

**Exit:** Library checkout / DslCompiler entity emit green without EntitySyntaxMetadata.

### Phase S1 — Catalog unification — **Medium**

**Goal:** One index; kill key-mismatch class.

1. Design `DomainCatalogMetadata` (or promote MTI + DTLM/RLM into one record).  
2. Produce it in **one** early pass (post-structure validate, or split Semantic into Validate + Catalog).  
3. Retarget:

   - `DomainSemanticLookupExtensions`  
   - `RuntimeContractAnalyzer` products that only re-index  
   - Evolution `DomainMutationContext`  
   - MCP describe  

4. Keep **stage-keyed** `SubscriptionDispatchPlanMetadata` if needed for notify identity, but build it from catalog.  
5. Deprecate duplicate fields; one release of dual-write if needed, then delete.

**Exit:** Grep shows single publisher for entity/action/policy maps; DACR residual scans only where analysis is legitimately null.

### Phase S2 — Collapse derived-view redundancy — **Medium**

**Goal:** One story for “effective actions/policies at a stage.”

1. Choose **Capability** (or Behavior) as the single “effective surface” view for describe/export.  
2. Align `GetEffectivePolicies` / DescribeStage with that view (already partially StageCapabilityMetadata).  
3. BehaviorPass becomes a thin adapter for pack-shaped DTOs **or** merges into Capability.  
4. Fix Capability transition targets to real `Stage` references via catalog.

**Exit:** No three-way policy composition algorithms.

### Phase S3 — Validation vs facts split — **Large / incremental**

**Goal:** EffectAnalyzer / PolicyConstraintAnalyzer stop being opaque megapasses.

1. Split each into:  
   - **Fact emitters** (small metadata only where runtime/export needs it)  
   - **Diagnostic rules** (optional or severity-tiered packs)  
2. Declare real `Dependencies`.  
3. Move remaining Lowering-owned domain-fact algorithms fully into Analysis (finish DAU).

**Exit:** EffectAnalyzer size drops; metadata products listed in CORE “required for X” tables.

### Phase S4 — Residual scan removal — **DACR Done Definition**

**Goal:** Suite Done Definition item 4.

1. AnalysisResult required on all semantic runtime/MCP/export paths.  
2. Delete `DM-META-REMOVE-FALLBACK` sites.  
3. EntitySyntax dual-path comments die with S0.

**Exit:** Zero fallback markers in DomainModeling + OracleTool (+ DslCompiler as applicable).

---

## 8. Suggested ownership matrix (after simplification)

| Consumer need | Required metadata | Producing pass (target) |
|---|---|---|
| Resolve type by name | Catalog | Catalog pass |
| Resolve relationship | Catalog | Catalog pass |
| Resolve action (SA) | Catalog (+ stage context) | Catalog + small SA helper |
| Notify subscribers | RCM + ESM + SDPM | Catalog + Subscription plan builder |
| Describe stage/policy | Catalog + Capability | Catalog + Capability |
| Create-in lowering | ResolvedRelationshipTarget **or** Catalog | Effect facts / Catalog |
| Storage/DB emit | Ownership + Topology + Storage | Ownership, Topology, Storage |
| Entity C# files | AnalysisResult only | **Export**, not a pass |
| Evolution mutate | Catalog | Catalog |

---

## 9. Non-goals

- Rewriting the Analysis **framework** (`Poly/Analysis`)  
- Merging interpretation AST passes with domain passes  
- Full SQL dialect packs in core defaults  
- Deleting pack-facing Storage/Transport concepts (only dual homes and redundant indexes)  
- Completing all diagnostic tiers in S0–S1  

---

## 10. Immediate recommendations (this week)

| Priority | Action |
|---|---|
| P0 | **S0**: pull EntitySyntax out of the pipeline; call projection from DslCompiler on finished analysis |
| P0 | Stop casting `INodeMetadataProvider` → `AnalysisResult` for GetMetadata in export helpers |
| P1 | Document pass table (this file) in `docs/CORE.md` “Domain analysis” pointer |
| P1 | Start **S1** design note for single catalog (MTI + DTLM/RLM merge sketch + consumer list) |
| P2 | Do not add new metadata bags without naming the **single** consumer and whether Catalog already covers it |
| P2 | New DACR work only against Catalog / declared required bags — no new dual indexes |

---

## 11. Appendix — LOC and dependency honesty

| Pass | Approx LOC | Deps declared? | Produces durable metadata? |
|---|---|---|---|
| EffectAnalyzer | ~990 | No | Minimal (one node metadata type) |
| PolicyConstraintAnalyzer | ~470 | No | RequiredProperties |
| StorageAnalyzer (algo) | ~360 | n/a | via StoragePass |
| SemanticDomainAnalyzer | ~220 | No | **Yes** (core indexes) |
| RuntimeContractAnalyzer | ~200 | Partial | **Yes** (runtime maps) |
| ConstraintPropagationAnalyzer | ~220 | No | DownstreamConstraints |
| DomainSemanticLookupExtensions | ~210 | n/a | Consumer API |
| SubscriptionAnalyzer | ~420 | Partial | Mostly diagnostics |
| Most other passes | 70–160 | Mixed | Yes or diagnostics-only |
| EntitySyntaxPass | ~35 | No | Projection bag (mis-tiered) |

---

## 12. Success metrics

1. Entity emit works without `EntitySyntaxMetadata` mid-pipeline.  
2. ≤ **1** authoritative catalog for name→member resolution.  
3. ≤ **1** algorithm for “effective policies at stage.”  
4. Every pipeline pass has accurate `Dependencies` or is explicitly order-free pure lint.  
5. Zero `metadata as AnalysisResult` in shared projection helpers.  
6. DACR fallback marker count trends to 0 with green suite.  
