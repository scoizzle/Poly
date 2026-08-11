# DomainModeling metadata simplification pass — 2026-08-10

**Kind:** Survey + executed cuts. 
**Method:** reachability analysis (`rg` for product/test callers), LOC survey, pass/metadata consumer check. Every claim below cites the code.
**Scope:** `Poly/DomainModeling/**` (~19.6k LOC).

---

## Status

**Survey 2026-08-10; cuts EXECUTED 2026-08-10** (same day):
- **Part 1 #1** — `RelationshipCapabilityMetadata`/`RelationshipCapabilityView` deleted (dead pure copy).
- **Part 1 #2** — `EffectiveMemberMetadata` deleted; `DomainQueries.GetEntity` reads `entity.*` directly; exporter doc corrected.
- **Part 1 #3 + U1** — effective-policies consolidated to one producer: action-level effective policies folded into `ActionCapabilityView`; `EffectivePoliciesMetadata` + `PublishEffectivePolicies` removed; `SemanticDomainAnalyzer` slimmed to DTLM + RLM + type-ref + owner index; `BehaviorPass` consumes the capability surface.
- **Part 1 #4** — Transport retired: `TransportPass`/`TransportModel`/`TransportMetadata` deleted (MCP-only `hasTransport` boolean; no DslCompiler consumer); MCP summary field removed; 3 tests updated/removed.
- **Part 2 U2** — `OwnerEntityMetadata` (Action/Stage → entity) published once in `SemanticDomainAnalyzer`; `CapabilityAnalyzer` + `RuntimeContractAnalyzer` consume it (linear owner scans removed).
- **Part 2 U3a REVERTED** — `RuleCoverageAnalyzer` was switched to `ActionCapabilityMetadata.TransitionTargets`, but that view only holds *catalog-resolved* targets, so actions transitioning to a nonexistent stage silently lost their coverage hint (review M1). Reverted to the raw effect-walk presence check (the walk is already done for coverage). Lesson: resolved-target views are not transition-presence signals.

**Part 3 + follow-ups EXECUTED 2026-08-10** (same day):
- **G1** — `Domain.Redistribute` throws for orphan relationships (source entity not defined) instead of silently dropping them; test added.
- **G2** — `BehaviorPass` dead `action.Policies` fallback removed (capability always present in the pipeline).
- **D2** — `CreateInRelation` now carries the owning `StageName` (null for entity-level actions); `EffectTopologyPass` produces it.
- **D1** — derived back-ref + **`create in Rel` auto-wire**: shared `DomainToCSharpExporter.FindAutoWireBackReference` (exactly one singular nav on target pointing to source) is the single source of truth for both `AddCreateNavMethod` (wires `this`, excludes from factory params) and the call-site arg list. Ambiguous (multiple) or collection back-refs stay unset ctor params. Verified by compiling the repro (`Order.Create(title, total, this, …)`). This is the export-findings **R2 fix**; the DSL guide §0.3 now documents the auto-wire.
- **R6** — in-suite Roslyn compile oracle (`Microsoft.CodeAnalysis.CSharp`): `Export_Compiles_LibraryDomain` + `Export_Compiles_CreateInTargetWithCollectionNavs` compile the rendered export and assert zero errors — the CS1501 class now fails in CI.
- **#5 (`ResolvedTypeReferenceMetadata` on-demand) — NOT done, intentionally kept**: it is the resolve-once/consume-many upstream producer pattern this work promotes; on-demand derivation would re-derive resolution in 4 consumers and re-encode the "resolved" marker. **U3b** (`CapabilityAnalyzer.ResolveOwnerStages` fallback) — skipped, pass-ordering-blocked, low value.

Full suite green (1975).

---

## Consumer census (metadata → non-pass readers)

| Metadata | Non-pass consumers | Verdict |
|----------|--------------------|---------|
| `RelationshipCapabilityMetadata` | **none** | 🔴 dead — pure copy of the relationship node |
| `EffectiveMemberMetadata` | `DomainQueries.GetEntity` only | 🟠 redundant — pure copy of `entity.*` |
| `EffectivePoliciesMetadata` | `BehaviorPass` only | 🟠 third producer of the same composition |
| `TransportMetadata` / `TransportSurface` / `TransportEntity` | MCP `hasTransport` boolean only | 🟠 machinery for a null-check |
| `ResolvedTypeReferenceMetadata` | 3 analyzers | 🟡 on-demand-derivable cache |
| `StorageModel` | DslCompiler `DbContext/HttpFile/MinimalApi` + MCP | ✅ product-path (codegen) |
| `BehaviorModel` | DslCompiler `HttpFile/MinimalApi` + MCP | ✅ product-path |
| `AggregateModel` | DslCompiler `HttpFile/MinimalApi` + MCP | ✅ product-path |
| `EffectTopology` | StorageAnalyzer, OwnershipAggregate, Transport, EntityStructure | ✅ feeds the above |
| `EntityStructureMetadata`, `RelationshipLookupMetadata`, `DomainCatalogMetadata`, `StageCapabilityMetadata`, `ActionCapabilityMetadata`, `RequiredPropertiesMetadata` | runtime / exporter / lowering / evolution / MCP | ✅ core |

---

## 🔴 1. `RelationshipCapabilityMetadata` — dead metadata

`CapabilityAnalyzer` (`CapabilityAnalyzer.cs:24-35, 62-64, 83-85, 187-198`) publishes `RelationshipCapabilityMetadata(RelationshipCapabilityView)` per relationship. The view is a **pure copy** of the `Relationship` node (`RelationshipName`, `Source`, `Target`, `Cardinality`, `Properties`, `Stages`, `Policies`). **Zero consumers** — the relationship node already carries this data.

**Delete:** the `RelationshipCapabilityView` + `RelationshipCapabilityMetadata` records, `AnalyzeRelationship`, the `case Relationship` dispatch arm, and the relationship iteration in `AnalyzeDomain`.

## 🟠 2. `EffectiveMemberMetadata` — pure copy, one reader

`SemanticDomainAnalyzer.PublishEffectiveMemberMetadata` (`SemanticDomainAnalyzer.cs:170-177`) publishes a copy of `entity.Properties/Actions/Policies/Stages`, with the comment *"Without entity inheritance, effective members are just the entity's own members."* There is **no inheritance** in the DSL. The only reader is `DomainQueries.GetEntity` (`DomainQueries.cs:159`), which already falls back to `entity.*` when the bag is absent.

**Delete the metadata; `DomainQueries.GetEntity` reads `entity.*` directly** (drops the `?? entity.*` fallbacks). The exporter's doc comment claiming the bag is required (`DomainToCSharpExporter.cs:41-48`) is stale — it never actually reads it. §6: the inheritance seam has zero real uses; extract when inheritance exists.

## 🟠 3. Effective-policies composition — three producers of one algorithm

The stage-effective policies/actions composition is implemented three times:
1. `DomainEffectiveSurface` (canonical helper — `ComposeStagePolicies`, `ComposeStageActions`).
2. `CapabilityAnalyzer` → `StageCapabilityMetadata.View.EffectivePolicies/EffectiveActions`.
3. `SemanticDomainAnalyzer.PublishEffectivePolicies` → `EffectivePoliciesMetadata` (entity/action/stage), comment *"Same algorithm as Capability / GetEffectivePolicies."* (`SemanticDomainAnalyzer.cs:155`).

`EffectivePoliciesMetadata` is read by **one** consumer: `BehaviorPass` (`BehaviorPass.cs:58`) for action-level effective policy *names* — and BehaviorPass already falls back to `action.Policies`. The stage-level part duplicates `StageCapabilityMetadata`.

**Consolidate:** one producer. Either fold action-level effective policies into `ActionCapabilityView` (BehaviorPass already reads that surface for transition targets) and drop `EffectivePoliciesMetadata` + `PublishEffectivePolicies`, or have `BehaviorPass` compose via `DomainEffectiveSurface` with the owner entity resolved from the catalog. Removes a third copy of the composition and the hand-maintained coupling.

## 🟠 4. Transport pass/model — machinery for a null-check

`TransportPass` + `TransportModel`/`TransportSurface`/`TransportEntity` + `TransportMetadata` compute a protocol-convention view (per-entity `ParentName` routing context, `IsExposable`) for **every** analyzed domain. Consumers:
- **DslCompiler: none.** `HttpFileGenerator`/`MinimalApiGenerator` read `AggregateModel`/`BehaviorModel`/`StorageModel`, never `TransportSurface`/`TransportEntity` (verified: no `Transport*` in `src/`).
- **MCP `DomainTools.cs:314`:** `hasTransport = GetMetadata<TransportMetadata>(domain) is not null` — a null-check.
- Tests: `PipelineMergeMetadataTests`, `InfrastructureAnalyzerTests`, `DomainModelAnalyzerContextTests` assert it exists.

This is the classic "derived fact with a single weak consumer." **Decision needed:** remove the pass + model + metadata and drop `hasTransport` from the MCP summary, or keep only if HTTP/protocol generation is on the immediate roadmap. Storage/Behavior/Aggregate are **not** in scope — they feed real codegen.

## 🟡 5. `ResolvedTypeReferenceMetadata` — per-node cache

`SemanticDomainAnalyzer.ResolveTypeReference` (`SemanticDomainAnalyzer.cs:106-126`) stores the resolved `DomainType` on every `DomainTypeReference` node. Consumers (`EffectAnalyzer`, `ConstraintQualityAnalyzer`) look it up. It duplicates the `DomainTypeLookupMetadata.Types` dict. **Candidate:** derive on demand via the dict; drop the per-node publication. Low value, low risk — the cache is small and harmless; only worth it if a pass consolidation makes the dict the single lookup.

## Non-findings (checked, intentionally kept)

- **Catalog dual-publish** (intermediate DTLM/RLM + embedded in `DomainCatalogMetadata`) — documented-intentional for mid-pipeline analyzers (CORE DAS W1).
- **MTI/ARM in the catalog** — read by evolution + runtime; this is the "sole name→member publisher" pattern working as designed.
- **Derived-facts chain** (OwnershipAggregate → Storage/Behavior → codegen) — real consumers; only the Transport link (#4) is consumerless.
- `RequiredPropertiesMetadata` vs `DownstreamConstraintsMetadata` — two distinct concerns (required-coverage vs effect-propagated constraints), both feeding `EffectAnalyzer`; not duplicative.

---

## Recommendation order

1. **Delete `RelationshipCapabilityMetadata`** (#1) — dead, pure delete + test for no-regression.
2. **Delete `EffectiveMemberMetadata`** (#2) — one reader, pure copy; update `DomainQueries` + the exporter doc.
3. **Consolidate effective-policies** (#3) — one producer; remove `EffectivePoliciesMetadata` + `PublishEffectivePolicies`.
4. **Decide Transport** (#4) — remove or keep-on-roadmap.
5. **Optional:** `ResolvedTypeReferenceMetadata` on-demand derivation (#5).

---

# Part 2 — pass structure: upstream-producer unifications

Lens: which derivations are re-computed per pass instead of produced once upstream and consumed downstream. The pipeline is already mostly well-factored in this direction (see Non-findings below); the remaining consolidation:

## U1. Effective surface → single upstream producer (extends #3)

After #2/#3, `CapabilityAnalyzer` becomes the **sole producer** of the effective surface:
- Fold **action-level effective policies** (entity+action / stage+action) into `ActionCapabilityView` (currently it carries `ActionName/Parameters/Effects/EffectTypes/TransitionTargets`; `BehaviorPass` already reads that bag for transition targets and would read `EffectivePolicies` from the same place — dropping its `EffectivePoliciesMetadata` read at `BehaviorPass.cs:58`).
- `DomainQueries`, `SubscriptionAnalyzer`, `RuleCoverageAnalyzer`, `BehaviorPass` all consume the capability surface instead of re-composing.
- `SemanticDomainAnalyzer` slims to its core: **DTLM + RLM + type-reference resolution**. It stops being an effective-surface producer entirely (today it publishes `EffectiveMemberMetadata` + `EffectivePoliciesMetadata`).

This mirrors the pattern the repo already applies elsewhere: `EffectFactsPass` → `EffectAnalyzer`, RLM as the single relationship producer, the catalog as the sole name→member publisher.

## U2. Reverse owner-entity index (new upstream producer)

`Action → Entity` / `Stage → Entity` reverse lookup is hand-scanned in at least:
- `CapabilityAnalyzer.FindOwnerEntity` / `FindOwnerEntityForStage` (`CapabilityAnalyzer.cs:142-159`) — linear scans.
- `RuntimeContractAnalyzer.FindStageOwnerEntity` (`RuntimeContractAnalyzer.cs`) — `FirstOrDefault(entity => entity.Stages.Contains(stage))`.

Publish the reverse index once — the catalog MTI already holds the forward `ActionsByEntity` / `StagesByEntity`; add the inverse (`Action → Entity`, `Stage → Entity`) to the MTI or `EntityStructureMetadata`. Value is **single source of truth**, not performance (domains are small).

## U3. Minor: consume existing upstream facts instead of re-deriving

- `RuleCoverageAnalyzer.cs:37` re-derives `hasStageTransition` via `FlattenEffects(...).Any(e is StageTransitionEffect)`; `ActionCapabilityMetadata.TransitionTargets` already exists — consume it.
- `CapabilityAnalyzer.ResolveOwnerStages` maintains its own stage-map fallback; the canonical per-entity stage map is `EntityStructureMetadata.StageByName` / `TryGetStage` (`DomainSemanticLookupExtensions.cs:88`) — use it.

## Merging passes is mostly NOT the win

The pipeline is correctly split into **fact-emitters** (Semantic, Catalog, RequiredProperties, EffectFacts, Capability, Topology, Aggregate, Behavior, Storage) + **linters** (Structural, PolicyConstraint, EffectAnalyzer, ConstraintQuality, RuleCoverage, ContractIntegration, Subscription, AuthoringSuggestion). The producer/consumer split is the right shape. Merging linters would erode diagnostic granularity (§7). Two micro-notes:
- `TransportPass` removal (Part 1 #4) resolves itself.
- `OwnershipAggregatePass` + `BehaviorPass` are both small derived-fact producers and could merge, but they consume different upstream facts (topology vs capabilities) — keep separate; marginal.

## Non-findings (pattern already applied — the model to follow)

- `DomainAnalysis` iteration helpers (`ForEachEntity`/`ForEachAction`) — boilerplate extraction done.
- `EffectFactsPass` → `EffectAnalyzer` — create-in resolution produced once, consumed by the linter.
- RLM single producer — relationship resolution consolidated (2026-08-10 slice).
- Catalog sole name→member publisher — DTLM/RLM/MTI/ARM produced once.
- `RequiredPropertiesPass` → `RuleCoverageAnalyzer` + `EffectAnalyzer` — required-coverage produced once.

---

# Part 3 — gaps surfaced by the consolidation (diagnostics + metadata discovery)

## Metadata discovery (facts that should be produced upstream but aren't)

### D1. Derived back-reference — still hand-derived per pass
The inverse of `(S, name → T)` — "the nav on `T` whose target is `S`" — is the ADR's "back-references are derived" (accepted, unimplemented). Today it's hand-derived in:
- `OwnershipAggregatePass.cs:103` — `relationships.FirstOrDefault(source == child && target == parent && singular)`.
- `CrossReferencePass.cs:63` — inverse-pair heuristic (`relationshipPairs.Contains((to, from))`).

An upstream back-ref index (or a `DerivedBackReference` fact published once) would serve the aggregate pass, the cycle pass, **and** the `create in Rel` auto-wire fix (export findings R2). This is the highest-value missing fact.

### D2. Stage-scoped create-in — `CreateInRelation` lacks the owning stage
`EffectTopology.CreateInRelation(CreatorEntity, ActionName, RelationshipName, CreatedEntity)` has no stage, but the stage is derivable (the action's owning stage). Consumers that want "S creates T via R while in stage X":
- `OwnershipAggregatePass.cs:129` — parent selection from create-in.
- Export wiring (which stage's actions create which children).

Producing stage-scoped create-in once would let the aggregate pass stop picking `FirstOrDefault` heuristics.

## Diagnostic gaps

### G1. Orphan relationships are silently dropped
`Domain.Redistribute` (bridge ctor) only attaches rels whose `Source.TypeName` matches a known entity; a `new Domain(name, types, [rel])` where `rel.Source` isn't in `types` **silently loses the relationship** (fail-open on model construction). **Fix: throw `ArgumentException` in the bridge ctor for an unattached relationship** — fail closed.

### G2. BehaviorPass silent degradation on missing capability
When `ActionCapabilityMetadata` is absent, `BehaviorPass.BuildBehaviorAction` falls back to `action.Policies`, silently losing entity/stage-level effective policies. Unreachable in the full pipeline (CapabilityAnalyzer always publishes); effectively dead fallback. Either hard-require the capability or delete the fallback with a comment.

## Non-gaps (verified already covered)

- create-in initializer binding an unknown property → `EffectAnalyzer` (DMEFF error).
- `require` gate referencing an unknown policy → parser (`ResolvePendingRequires` throws).
- `when Rel Stage` unknown relationship/stage → `SubscriptionAnalyzer`.
- Quantifier relationship cardinality/source → `PolicyConstraintAnalyzer`.
- Duplicate nav names / type-name collisions → parser + structural.

