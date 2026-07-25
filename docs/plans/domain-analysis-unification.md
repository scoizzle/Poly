# Domain Analysis Unification — Task Plan

**Date:** 2026-07-25  
**Status:** Active — successor to APM (registration complete; ownership + pipeline unfinished)  
**Micro-tasks:** [`simple-agent-tasks/dau-README.md`](simple-agent-tasks/dau-README.md)  
**Related:** [`analysis-pipeline-merge.md`](analysis-pipeline-merge.md) (complete; design reference) · [`domainmodeling-capability-inventory.md`](../domainmodeling-capability-inventory.md) · [`docs/CORE.md`](../CORE.md) · pack experiment [`dsl-plugin-pipeline-experiment.md`](dsl-plugin-pipeline-experiment.md)

---

## 1. Context (read this first)

### What APM finished

Topology, aggregate, behavior, and cross-reference **run on every domain analyze** (`UseDomainModelAnalysisPipeline`). DslCompiler no longer re-runs those three for domain facts. Diagnostics DMAGG001 / DMDEP001 / DMBEH001 exist with fixtures.

### What APM did *not* finish

The move was **registration-first**. Algorithms and models largely still live under `Lowering/`, with thin `Analysis/*Pass` wrappers. That produces **mid-migration artifact mismatch**:

| Symptom | Reading |
|---------|---------|
| `*Pass` → `new Lowering.*Analyzer` | Temporary bridge, not long-term shape |
| Domain facts under `Lowering/` | Old home; Analysis is the target home |
| Storage / Transport only on codegen path | Not fully integrated into core domain analysis yet |
| Surfaces with few `GetMetadata` readers | **Wiring lag / pack-bound**, not automatic delete candidates |
| Docs: “codegen-only”, “aggregate stays in lowering” | Stale vs in-flight migration |

**Do not** treat today’s dual shape as intentional architecture. **Do not** purge pack-facing surfaces because codegen/MinimalApi does not read them yet.

### Direction of travel

```text
Domain evolve / MCP analyze
  → UseDomainModelAnalysisPipeline()   ← owns ALL domain facts
  → AnalysisResult (structure, coupling, actions, storage shape, transport shape, …)

Packs / session (DomainAuthoringContext)
  → type maps, storage conventions, protocol refinements, PassRegistry extras
  → refine or extend the same AnalysisResult story

Codegen (DslCompiler)
  → emit from analysis metadata (+ pack-refined bags)
  → not a second full analysis world for domain facts

Lowering/
  → DE→AST, effect lowering, PolicyEvaluator (true lower/execute)
  → may *consume* Analysis metadata; must not *own* domain-fact algorithms long-term
```

**Hard dependency rule:** **Lowering depends on Analysis** — not the reverse via thin wrappers.

---

## 2. Goals

1. **One home** for domain-fact analysis: `Poly/DomainModeling/Analysis/`.
2. **No long-lived thin wrappers** — each fact is one `INodeAnalyzer` (algorithm + metadata + diagnostics).
3. **Unify overlapping walks** (root/ownership, action shape, coupling, effects, subscriptions).
4. **Storage + transport in domain analysis** — almost always useful; pack binding via authoring context, core defaults when unbound.
5. **Pack-ready seams** — do not delete transport / coupling bags / capability facets packs will use; shape them so packs plug in.
6. **Codegen thins to emit** — reuses domain `AnalysisResult`; pack passes only where refinement is required.
7. **Docs match reality** — CORE, inventory, DomainModeling README, plans.

### Non-goals

- Bar B / full string-oracle parity  
- Inventing RestApiSurfacePass “for completeness” without a pack consumer design  
- Moving **PolicyEvaluator** / DE lowering into Analysis (they stay Lowering)  
- Always-on **dialect-specific** SQL without session context (wrong defaults)  
- Entity inheritance revival  
- Second product IR  

---

## 3. Pack and “unused” policy

| Kind | Examples | Rule |
|------|----------|------|
| **Pack-bound / soon real** | Transport surface, storage enrichment, dependency/coupling graph, action/capability facets packs will extend | **Keep concept**; unify home and API; document extension point |
| **Migration residue** | Thin Pass wrappers, dual root heuristics, Lowering dual types after move | **Absorb or delete** once Analysis owns the algorithm |
| **Proven dead** | EnumConstraintSubset after inheritance removal; fixed-point path that never runs | **Delete** with tests that inheritance stays gone |

**Default:** missing consumer ⇒ **incomplete wiring**, not delete — unless residue is proven (inheritance, empty fixed-point).

---

## 4. Target shape

### Always-on domain pipeline (conceptual)

```text
Structural → Semantic → PolicyConstraint
→ Effect (binding + ordering + unused-param — merged walks)
→ Constraint (quality [+ propagation])
→ Capability / ActionModel (incl. former Behavior projection)
→ EntityStructureOwnership (local structure + aggregate graph)
→ EntityCoupling (topology facets + cycle diagnostics)
→ Subscription (contract + causality + replay)
→ ContractIntegration (if still distinct)
→ Storage (defaults or session type maps / conventions)
→ Transport (domain exposable surface; pack refines later)
→ AuthoringSuggestion → EntitySyntax
```

Exact class names may differ; **count of concepts** should drop (~22 → ~12–14 always-on registrations).

### Codegen / packs

```text
domainAnalysis = Analyze(domain, authoringContext?)

// Prefer metadata already on domainAnalysis:
storage  = domainAnalysis.GetMetadata<StorageMappingMetadata>(domain)
transport = domainAnalysis.GetMetadata<TransportMetadata>(domain)

// Only if pack needs a *refinement* pass not expressible as conventions:
//   small AnalyzerBuilder + priorAnalysis: domainAnalysis + pack passes
// Do NOT re-run topology/aggregate/behavior/ownership.
```

### Lowering/

| Keep | Move out (to Analysis) |
|------|-------------------------|
| `DomainExpressionLoweringPass` | `EffectTopologyAnalyzer` (+ model) |
| `EffectLoweringPass` | `AggregateAnalyzer` (+ model) |
| `PolicyEvaluator` | `BehaviorAnalyzer` (+ model) — or fold into Capability |
| `DomainToCSharpExporter` / IR helpers | (logic into Analysis analyzers) |
| Storage **implementation** may live under Analysis or stay as Analysis-owned type | `TransportAnalyzer` → Analysis |
| Pack type maps / conventions types | — |

---

## 5. Phases and micro-tasks

**Queue:** [`simple-agent-tasks/dau-README.md`](simple-agent-tasks/dau-README.md)

Execute **in order** within a phase unless noted. Gate before claiming phase Done.

### Phase 0 — Framing (docs only, small)

| ID | Task | Outcome |
|----|------|---------|
| **D0.1** | Mark APM plan successor + inventory “in flight” note | Agents stop treating APM as final architecture |
| **D0.2** | Add “do not delete pack surfaces” + migration residue note to inventory §5 | Honest product map |

### Phase 1 — Collapse wrappers (no behavior change)

**Goal:** Analysis owns algorithms; delete Lowering duals for domain facts already on the pipeline.

| ID | Task | Notes | Diff |
|----|------|-------|------|
| **D1.1** | Move `EffectTopologyAnalyzer` + `TopologyModel` (types) into `Analysis/`; `EffectTopologyPass` becomes the real analyzer body (or rename to `EffectTopologyAnalyzer : INodeAnalyzer`) | Update all usings; keep metadata API | M |
| **D1.2** | Same for aggregate: `AggregateAnalyzer` + `AggregateModel` → Analysis; absorb `OwnershipAggregatePass` | Keep DMAGG001; Dependencies unchanged in spirit | M |
| **D1.3** | Same for behavior: `BehaviorAnalyzer` + `BehaviorModel` → Analysis; absorb `BehaviorPass` | Still may be temporary until D2.2 | M |
| **D1.4** | Retarget tests (`InfrastructureAnalyzerTests`, `GenerationAssertions`, pack tests) to Analysis types or `DomainModelAnalyzer.Analyze` | Prefer domain analyze for multi-fact tests | M |
| **D1.5** | Gate: full suite green; no `Analysis/*Pass` calling `Poly.DomainModeling.Lowering.*Analyzer` for topo/agg/beh | Pre-ship residual | S |

**Exit 1:** Zero thin wrappers for topology/aggregate/behavior; Lowering no longer owns those algorithms.

### Phase 2 — Unify overlapping cores

**Goal:** One walk / one root / one action shape / one coupling story where today we double-scan.

| ID | Task | Merge | Notes | Diff |
|----|------|-------|-------|------|
| **D2.1** | **Root + ownership** | `EntityStructureAnalyzer` + ownership/aggregate | Single root definition; local key/soft-delete/stages + parent graph; one or two metadata bags from one analyzer family | M |
| **D2.2** | **Action shape** | Capability + Behavior projection | One action/stage fact publisher; BehaviorModel may become projection over metadata or fold into Capability; drop unread RelationshipCapability *only if* packs don’t need it — prefer keep facet, stop double walk | M |
| **D2.3** | **Coupling** | Effect topology + CrossReference | One coupling analyzer: create-in/invoke/subs + cycle diagnostic; keep topology facets packs/codegen need; don’t delete graph concept for “no reader” | M |
| **D2.4** | **Effects** | Fold `EffectOrderingAnalyzer` + `ActionParameterUsageAnalyzer` into `EffectAnalyzer` (shared flatten walk) | Keep diagnostic codes; fewer registrations | S–M |
| **D2.5** | **Subscriptions** | Contract + Causality + ReplaySafety → one `SubscriptionAnalyzer` (sections) | Keep codes; one subscription walk | M |
| **D2.6** | Gate Phase 2 | Suite + PipelineMerge diagnostic goldens + AllMode | | S |

**Exit 2:** Fewer registrations; no dual root; no Capability+Behavior double entity walk; coupling single home.

### Phase 3 — Storage + transport in domain analysis

**Goal:** Always-useful operational facts on every domain analyze; packs refine via context.

| ID | Task | Notes | Diff |
|----|------|-------|------|
| **D3.1** | Design API: `DomainModelAnalyzer.Analyze(Domain, DomainAuthoringContext? context = null)` (or pipeline factory on context) | Cached pipelines keyed by context identity/fingerprint if needed; null context ⇒ core defaults | M |
| **D3.2** | Move/absorb `StorageAnalyzer` into Analysis as real `INodeAnalyzer`; register on domain pipeline | Type maps + conventions from context; defaults when null; Dependencies on ownership + topology (or unified successors) | M |
| **D3.3** | Move/absorb `TransportAnalyzer` into Analysis; register on domain pipeline | Baseline exposable surface from ownership/actions; **do not delete** for packs | M |
| **D3.4** | Thread MCP / DSL session context into domain analyze (already `CreateWithSqlPack` on MCP) | Evolution path should pass the same context used for parse when available | M |
| **D3.5** | DslCompiler: prefer storage/transport from **domain** `analysis`; remove re-run of domain-fact passes; pack `PassRegistry` only for true refinements | Fail-closed messages point at domain pipeline + packs | M |
| **D3.6** | Tests: domain analyze has Storage (+ Transport) metadata; pack tests still differ SQLite vs generic; AllMode green | | M |
| **D3.7** | Gate Phase 3 | Pre-ship; update inventory §5.1/§5.2 | S |

**Exit 3:** `DomainModelAnalyzer.Analyze` (with session context when available) produces storage + transport metadata; codegen is emit-first.

### Phase 4 — Residue + docs

| ID | Task | Notes |
|----|------|-------|
| **D4.1** | Delete **proven** residue only: `EnumConstraintSubsetAnalyzer`, unreachable fixed-point / DMCS002 if still dead | Not Transport |
| **D4.2** | CORE + DomainModeling README + inventory: Analysis owns domain facts; Lowering = lower/execute; packs refine | |
| **D4.3** | Optional: drop `UseDomainModelValidation` alias docs noise; naming Pass→Analyzer consistency pass | Hygiene |
| **D4.4** | Final suite gate + mark plan Complete | |

---

## 6. Verification (every phase)

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

Minimum focused checks:

| Check | Why |
|-------|-----|
| `PipelineMergeMetadataTests` / `DomainAnalysis_*` | Domain facts + diagnostics |
| `DomainAnalysis_HasInfraMetadata_CodegenProducesStorage` (or successor) | Domain vs codegen split honesty |
| AllMode / DbContext / MinimalApi IR tests | Emit still works |
| Sqlite vs generic pack tests | Pack refinement still differs |
| MCP smoke if session analyze path changed | Context threading |

Pre-ship: [`pr1-uncommitted-review-gate.md`](v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md).

---

## 7. Risks

| Risk | Mitigation |
|------|------------|
| Silent root/policy regression while merging | Keep context-first metadata tests; golden root/child fixtures |
| Pack results differ after always-on storage | Thread same `DomainAuthoringContext` as parse; defaults documented |
| Large PR thrash | One phase per PR; D1 before D2 before D3 |
| Over-delete pack surfaces | §3 policy; review gate must ask “pack or residue?” |
| Evolution cost (storage every Apply) | Accept for coherence; measure later; structural failure short-circuit stays |
| Circular deps Analysis ↔ Lowering | Models in Analysis; Storage may need DomainTypeMapping (already shared); DE lowering stays Lowering-only |

---

## 8. Agent pick

```text
DONE:    APM registration; DAU plan + D0 framing
CURRENT: D1.1 — topology algorithm + model → Analysis (collapse wrapper)
THEN:    D1.2–D1.5 → D2 unify walks → D3 storage/transport → D4
PULL:    Bar B; RestApi pack design; dialect packs beyond generic/SQL annotations
```
---

## 9. Relationship to prior plans

| Plan | Relation |
|------|----------|
| [`analysis-pipeline-merge.md`](analysis-pipeline-merge.md) | **Predecessor** — registration done; this plan finishes ownership + storage/transport + unify walks |
| [`infrastructure-pass-NEXT.md`](infrastructure-pass-NEXT.md) | Archive for IR/codegen bar; do not reopen dual infra analysis |
| Pack / plugin experiments | Consume unified Analysis metadata; do not fork a third pipeline |

---

## 10. Success criteria

- [ ] No domain-fact algorithm remains only under `Lowering/` with a thin Analysis Pass wrapper  
- [ ] Lowering does not own aggregate/topology/behavior/transport algorithms  
- [ ] Overlapping walks reduced (root, actions, coupling, effects, subscriptions)  
- [ ] Storage + transport metadata available from domain analysis (context-aware packs)  
- [ ] DslCompiler does not re-derive domain facts  
- [ ] Pack-facing surfaces retained or explicitly redesigned — not deleted as “unused”  
- [ ] CORE + inventory + README match the tree  
- [ ] Full suite green; pre-ship clean  
