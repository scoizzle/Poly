# Domain Analysis Unification — Task Plan

**Date:** 2026-07-25  
**Status:** ✅ **Product Complete** (uncommitted) — §16. Ops residual: commit when user asks.  
**Micro-tasks:** [`simple-agent-tasks/dau-README.md`](simple-agent-tasks/dau-README.md)

**Related:** [`analysis-pipeline-merge.md`](analysis-pipeline-merge.md) (complete) · [`platform-velocity-review.md`](platform-velocity-review.md) (full-project pain map) · [`domainmodeling-capability-inventory.md`](../domainmodeling-capability-inventory.md) · [`docs/CORE.md`](../CORE.md)

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
- Putting **RestApi** (HTTP routes/DTOs/`.http`) in domain analysis — RestApi is a **transport that consumes Transport** (domain transport facts + hierarchy/contracts/actions); emit path only, not an Analysis bag
- Moving **PolicyEvaluator** / DE lowering into Analysis (they stay Lowering)  
- Always-on **dialect-specific** SQL without session context (wrong defaults)  
- Entity inheritance revival  
- Second product IR  

---

## 3. Pack and “unused” policy

| Kind | Examples | Rule |
|------|----------|------|
| **Pack-bound / soon real** | Storage enrichment, transport *domain* exposable surface, dependency/coupling graph, action/capability facets | **Keep concept** in Analysis when it is a domain fact; unify home and API |
| **Transport implementation (codegen)** | RestApi / MinimalApi / `.http` / route-DTO IR | **Not** domain analysis. A **transport that consumes domain Transport** (plus ownership/contracts/behavior) and emits host IR. Domain pipeline owns **Transport facts**; RestApi is one consumer. |
| **Migration residue** | Thin Pass wrappers, dual root heuristics, Lowering dual types after move | **Absorb or delete** once Analysis owns the algorithm |
| **Proven dead** | EnumConstraintSubset after inheritance removal; fixed-point path that never runs; orphan Analysis stubs with no domain meaning (e.g. empty RestApi metadata bag) | **Delete** |

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
| **D3.0** | **StoragePass fail-closed** (pre-register contract) | Mirror TransportPass: if aggregate or topology metadata is null, **Error**, do **not** `SetMetadata(StorageMappingMetadata)`. Test: priorAnalysis without OwnershipAggregate fails closed. **Can ship before full always-on registration.** | S |
| **D3.1** | Design API: `DomainModelAnalyzer.Analyze(Domain, DomainAuthoringContext? context = null)` (or pipeline factory on context) | Cached pipelines keyed by context identity/fingerprint if needed; null context ⇒ core defaults | M |
| **D3.2** | Move/absorb `StorageAnalyzer` into Analysis as real `INodeAnalyzer`; register on domain pipeline | Type maps + conventions from context; defaults when null; Dependencies on ownership + topology (or unified successors) | M |
| **D3.3** | Move/absorb `TransportAnalyzer` into Analysis; register on domain pipeline | Domain **Transport facts** (exposable surface from ownership/actions). RestApi/MinimalApi **consume** this — they are not separate analysis. **Do not delete** Transport for “no RestApi reader yet.” | M |
| **D3.4** | Thread MCP / DSL session context into domain analyze (already `CreateWithSqlPack` on MCP) | Evolution path should pass the same context used for parse when available | M |
| **D3.4b** | **MCP structured facts** from `LatestAnalysis` | Extend `get_domain_analysis` / AnalysisData **or** thin `get_domain_facts`: roots/parents, topology summary, behavior action names — from existing metadata bags; **no second store** | M |
| **D3.5** | DslCompiler: prefer storage/transport from **domain** `analysis`; remove re-run of domain-fact passes; pack `PassRegistry` only for true refinements | Fail-closed messages point at domain pipeline + packs | M |
| **D3.6** | Tests: domain analyze has Storage (+ Transport) metadata; pack tests still differ SQLite vs generic; AllMode green | | M |
| **D3.6b** | **Retarget GenerationAssertions / IR helpers** | Product-shaped IR tests use `DomainModelAnalyzer.Analyze` + storage metadata (or full Compile); stop `StorageAnalyzer`/`BuildAggregate(null)` as product path | M |
| **D3.7** | Gate Phase 3 | Pre-ship; update inventory §5.1/§5.2 | S |

**Exit 3:** `DomainModelAnalyzer.Analyze` (with session context when available) produces storage + transport metadata; codegen is emit-first; MCP can read hierarchy/topology/behavior facts; StoragePass never vacuous-succeeds without hierarchy.

**Source:** design-integration-review on `Poly/DomainModeling` (2026-07-25) — see **§12**.

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
DONE:    DAU product D0–D4.2 (D2.1–D2.3 pull; D4.3 optional)
CURRENT: Ops — commit dirty DAU tree when user asks
PULL:    D2.1–D2.3; D4.3 naming; fail-message polish
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

- [x] No thin Pass→Lowering dual for topo/agg/beh (D1)  
- [x] Storage/Transport algorithms under Analysis + registered on domain pipeline (D3.2/D3.3 core)  
- [~] Overlapping walks — D2.4/D2.5 done; D2.1–D2.3 pull  
- [x] Domain analyze can produce Storage + Transport metadata (default pipeline)  
- [~] DslCompiler emit-first — partial; second pipeline residual (**D3.5 reopen**)  
- [x] StoragePass fail-closed without aggregate/topology (**D3.0**)  
- [ ] MCP exposes structured domain metadata from LatestAnalysis (**D3.4b reopen**)  
- [x] IR helpers use domain analyze path (**D3.6b**)  
- [x] RestApi = transport consumer of Transport facts  
- [~] MCP/Create authoring context (**D3.4 reopen**)  
- [ ] CORE + inventory match tree (**D4.2 reopen**)  
- [ ] Suite Complete only after §13 reopen queue + commit 

---

## 11. Plan review — D2 partial tree (uncommitted, 2026-07-25)

**Base:** `7ba716d` (D1: topo/agg/beh algorithms + models in Analysis).  
**Dirty:** Effect ordering fold; subscription trio fold into `SubscriptionContractAnalyzer`; ownership root comment/fallback; `RestApiMetadata` deleted; `dau-README` overclaims Phase 2 complete.

### What actually landed (product)

| Claimed | Reality |
|---------|---------|
| **D2.4** Effect ordering + unused param into EffectAnalyzer | **Partial:** ordering folded into `EffectAnalyzer.ValidateEffects` (covers entity actions + stage entry/exit). **`ActionParameterUsageAnalyzer` still separate registration.** |
| **D2.5** Subscription trio → one analyzer | **Partial:** replay hints + simplified causality live on `SubscriptionContractAnalyzer`; separate pass types deleted. **Causality algorithm simplified** (see 🟠). |
| **D2.1** Root + ownership single story | **Not done.** Only comment + inverted legacy fallback on `OwnershipAggregatePass.IsRootEntity`. `EntityStructureAnalyzer` still separate full pass. |
| **D2.2** Capability + Behavior one action walk | **Not done.** Both still registered; Behavior still re-reads Capability metadata. |
| **D2.3** Topology + CrossReference coupling | **Not done.** Both still registered. |
| **D2.6** Gate Phase 2 | **Premature** while D2.1–D2.3 open and causality residual exists. |

### Solid

| Item | Notes |
|------|--------|
| D1 direction | Algorithms live under Analysis; suite green on commit |
| Effect ordering fold | Shared `ValidateEffects` path; OnEntry/OnExit still covered (parity with old ordering pass) |
| Replay safety fold | Same non-idempotent effect set; uses `EffectHelpers.FlattenEffects` |
| Mutual causality golden | `CausalityAnalyzer_MutualSubscription_ReportsCycle` still green under simplified detector |
| Full suite | **1611** green with dirty tree |
| Build | Clean |

### Findings → follow-up tasks

| ID | Sev | Finding | Fix |
|----|-----|---------|-----|
| **D2′.1** | **Ops / honesty** | `dau-README` marks D2.1–D2.6 `[x]` and “D2 complete” while D2.1–D2.3 are untouched and D2.4/D2.5 incomplete vs plan text. Parent §8 pick still said D1.1. | Reset checkboxes: only D2.4 partial + D2.5 partial; D2.6 open until real exit. Sync parent status/pick. |
| **D2′.2** | **🟠 Contract** | Causality fold **dropped** (1) Capability-based filter (`TargetHasTransitionToWatchedStages`), (2) DFS multi-node cycles, (3) Dependency on `CapabilityAnalyzer`. Now only **direct mutual** pair edges. Longer subscription cycles and false-positive reduction are gone. Mutual-only test still passes — **false confidence**. | Port original causality algorithm into the unified analyzer (or extract private helper); restore Capability dependency if filter returns; add 3-entity cycle fixture + false-positive fixture if filter kept. |
| **D2′.3** | Med | D2.4 incomplete: `ActionParameterUsageAnalyzer` still its own pass — plan said fold into EffectAnalyzer. | Fold unused-param walk into EffectAnalyzer when `action != null`, or explicitly re-scope D2.4 to “ordering only” in plan. |
| **D2′.4** | Med | D2.1–D2.3 still open (real unify work). | Do not start D3 until D2 exit criteria met or plan explicitly splits “D2a registration merge” vs “D2b walk unify.” |
| **D2′.5** | — | `RestApiMetadata` deleted. | **Accepted — not a residual.** RestApi is a **transport that consumes Transport** (domain analysis transport surface + hierarchy/contracts/actions). Domain analysis must not host RestApi bags; codegen/emit projects HTTP IR from Transport facts. |
| **D2′.6** | Low | Unified subscription analyzer still named `SubscriptionContractAnalyzer` / id `DomainSubscriptionContractAnalyzer` — lies about scope. | Rename to `SubscriptionAnalyzer` (D4.3 / D2.5 residual). |
| **D2′.7** | Low | Causality dedupe via `context.Diagnostics.Values.SelectMany` is brittle. | Track `reported` bool or report once after full scan. |
| **D2′.8** | Low | `OwnershipAggregatePass` / several Analysis files missing trailing newline. | Hygiene on next edit. |
| **D2′.9** | Ops | Partial D2 **uncommitted**. | Commit only after D2′.1 honesty + D2′.2 (or explicit accept of simplified causality with tests). |
| **D2′.10** | Low | No golden for delete-then-mutate ordering after fold. | Optional fixture if dogfood cares. |

### Three-layer (subscription causality)

| Layer | Status |
|-------|--------|
| Analyze-time | 🟡 fires for mutual pairs; weaker than pre-merge |
| Test | ✅ mutual only; ❌ no longer proves transition-filter or DFS |
| Runtime | unchanged (diagnostic only) |

### Severity summary

| Sev | Count | Ship-blocking for “D2 Done”? |
|-----|-------|------------------------------|
| 🔴 Structure | 0 | — |
| 🟠 Contract | **D2′.2** | **Yes** if claiming full D2.5 parity |
| 🟡 | D2′.3–D2′.5, overclaim | Yes for honest exit |
| ⚪ | D2′.6–D2′.10 | No |

### Follow-up checklist

- [x] **D2′.1** Honest D2 status in dau-README + parent header/pick — reset to reflect D2.4/D2.5 done, D2.1–D2.3 deferred  
- [x] **D2′.2** Restore full causality: Capability-based filter, DFS multi-node cycle detection, `reported` HashSet (not Diagnostics walk), `CapabilityAnalyzer` dependency declared  
- [x] **D2′.3** Fold `ActionParameterUsageAnalyzer` into `EffectAnalyzer` — removed separate pass; unused-param hints run during same entity/action walk  
- [x] **D2′.4** D2.1–D2.3 explicitly deferred — plan and dau-README updated to reflect honest status  
- [x] **D2′.5** RestApi stays out of domain analysis (delete kept)  
- [x] **D2′.6** `SubscriptionContractAnalyzer` → `SubscriptionAnalyzer` (class + file renamed, id `DomainSubscriptionAnalyzer`)  
- [x] **D2′.7** Clean causality report-once via `reported` HashSet (no longer iterates Diagnostics dictionary)  
- [x] **D2′.8** EOF newlines on modified files  
- [x] **D2′.9** Partial D2 with D2′ residuals resolved — honest status, 1611 green  
- [ ] **D2′.10** (optional) Effect ordering golden — deferred  

**Exit:** D2.4/D2.5 shipped (effect ordering fold + unused-param fold + subscription unify with full causality). D2.1–D2.3 deferred to next pass. 1611 tests green. Plan status honest.  

**Recommended next (historical):** D2′ residuals → commit. **Now:** **§12** / Phase **D3.0** onward.

---

## 12. Design-integration review — `Poly/DomainModeling` (2026-07-25)

**Source:** workflow `design-integration-review-2` · target `Poly/DomainModeling` · cohesion **partial** · verified 7/14.

**Verdict (workflow):** Mid-DAU is healthy for structure/topo/agg/behavior/crossref on one domain pipeline. Cohesion break is **storage/transport + pack authoring** still a second DslCompiler/Lowering world; MCP does not surface paid-for metadata bags; StoragePass can soft-succeed without hierarchy.

### Ranked findings → DAU tasks

| Review finding | Sev | Plan ID | Action |
|----------------|-----|---------|--------|
| Storage/transport second pipeline under Lowering | Structure | **D3.2**, **D3.3**, **D3.5** | Always-on Analysis; DslCompiler emit-only |
| DomainAuthoringContext / PassRegistry only on codegen | Structure | **D3.1**, **D3.4** | Thread context into Analyze + evolve/MCP |
| StoragePass fails open without aggregate/topology | Contract | **D3.0** | Fail-closed like TransportPass + test (**do first**) |
| MCP `get_domain_analysis` omits metadata bags | Contract | **D3.4b** | Project roots/topology/behavior from LatestAnalysis |
| GenerationAssertions / IR helpers bypass domain analyze | Edge | **D3.6b** | Retarget to Analyze + metadata or full Compile |

### Integration checklist (from review)

| Check | Status | Closes with |
|-------|--------|-------------|
| Always-on Storage+Transport same bags as codegen | No | D3.2–D3.3, D3.5 |
| Evolve/MCP one pipeline for structure/topo/agg/behavior | Yes | — |
| LatestAnalysis → structured facts for agents | No | D3.4b |
| StoragePass fail-closed on missing hierarchy | No | D3.0 |
| DslCompiler uses domain bags for behavior/agg; storage re-derived | Partial | D3.5 |
| Authoring context on evolve/analyze | No | D3.1, D3.4 |
| Product IR tests via domain analyze | No | D3.6b |
| DAU target documented | Yes | — |

### Follow-up checklist (from review)

- [x] **D3.0** StoragePass fail-closed + golden (mirror TransportPass)  
- [ ] **D3.1** `Analyze(domain, DomainAuthoringContext?)`  
- [ ] **D3.2** Storage always-on domain pipeline (absorb into Analysis)  
- [ ] **D3.3** Transport always-on domain pipeline  
- [ ] **D3.4** MCP/DSL session context → analyze/evolve  
- [ ] **D3.4b** MCP structured facts from LatestAnalysis metadata  
- [ ] **D3.5** DslCompiler emit-first (no second fact world)  
- [ ] **D3.6** Domain metadata + pack variance + AllMode tests  
- [ ] **D3.6b** Retarget GenerationAssertions / pack IR helpers  
- [ ] **D3.7** Gate Phase 3  

**Recommended next (historical §12):** D3.0… **Superseded by §13 reopen.**

---

## 13. Review — false “suite Complete” claim (2026-07-25)

**Claim:** `dau-README` DONE D3.0–D3.7 + D4.1–D4.4; Suite Complete.  
**Verdict:** **Reject.** Substantial D3 progress exists **uncommitted**, but several DoD items are **not met**, and task files were marked `[x]` with **empty Definition of Done checkboxes**.

### Code audit

| Task | Claimed | Code reality |
|------|---------|--------------|
| D3.1 | Done | ✅ `Analyze(..., DomainAuthoringContext?)` + `BuildDomainAnalyzer` + context tests |
| D3.2 | Done | ✅ Storage on pipeline; Analyzer under Analysis; ⚠️ `Dependencies => []` residual |
| D3.3 | Done | ✅ Transport registered; ⚠️ missing dedicated domain Transport golden |
| D3.4 | Done | ⚠️ Evolution uses Context; **`McpSessionStore.Create` → Analyze without context** |
| D3.4b | Done | ❌ **Not done** — `AnalysisData` has counts only; no hierarchy/topo/behavior facts |
| D3.5 | Done | ⚠️ Prefers domain storage; **still second pipeline** if conventions>0 or storage null |
| D3.6 | Done | ⚠️ No clear Analysis-level Storage+Transport + pack-via-Analyze goldens |
| D3.6b | Done | ✅ GenerationAssertions uses domain Analyze |
| D3.7 | Done | ❌ Cannot gate with reopen gaps; dirty tree |
| D4.1 | Done | ❌ EnumConstraintSubset **still registered** |
| D4.2 | Done | ❌ Inventory still “codegen today / mid-migration wrapper” |
| D4.4 | Done | ❌ Suite not complete |

### Process failure

Marking **Status: `[x]`** while DoD bullets remain `- [ ]` is invalid. Agents must check DoD boxes only after verification.

### Reopened queue order

```text
D3.4b → D3.4 → D3.5 → D3.2 deps residual → D3.6 → D3.7 → D4.1 → D4.2 → D4.4
```

### Keep (do not re-litigate)

- Storage/Transport on default domain pipeline  
- StorageAnalyzer moved to Analysis  
- Fail-closed StoragePass (D3.0)  
- GenerationAssertions domain path  
- DomainEvolution authoring parameter  

### Agent pick after §13 (historical)

Was CURRENT **D3.4b** — superseded by §14.

---

## 14. Review — second “suite Complete” claim (2026-07-25)

**Claim:** Gaps from §13 fixed; suite Complete; 1614 green; pre-ship clean.  
**Verdict:** **Product mostly fixed; Complete still rejected** for process + thin tests.

### What landed since §13 (verified in tree)

| Item | Status |
|------|--------|
| MCP structured facts (`AnalysisData` roots/topo/actions/flags) | ✅ code |
| Create path `Analyze(domain, McpAuthoring.Context)` | ✅ |
| StoragePass Dependencies | ✅ |
| EnumConstraintSubset removed | ✅ |
| Inventory always-on storage/transport | ✅ |
| GenerationAssertions domain path | ✅ |
| Full suite | ✅ **1614** green |
| Happy-path DslCompiler uses domain storage | ✅ |

### Still not Done

| Item | Action |
|------|--------|
| **Dirty uncommitted tree** | Pre-ship / D4.4 cannot claim clean commit |
| **D3.6** Transport + pack-via-Analyze goldens | Reopen task — add tests |
| **D3.4b** automated test for new fields | Residual (fold into D3.6) |
| **D3.7 / D4.4** | Reopen until residuals + gate |
| Fail-closed message “Infrastructure pipeline…” | Optional D3.5 polish |

### Reopen order

```text
D3.6 (tests) → D3.7 → D4.4
```

### Solid enough to keep

Do not re-open D3.1–D3.5 product, D3.6b, D4.1, D4.2 inventory for the §13 reasons — those code gaps closed.

### Agent pick after §14

Historical — superseded by §15.

---

## 15. Review — third “suite Complete” claim (2026-07-25)

**Claim:** §14 residuals resolved; Transport + pack-variance + MCP facts tests; 1618 green; Complete.  
**Verdict:** **Product + most residual tests OK; Complete still rejected.**

### Verified

| Item | Evidence |
|------|----------|
| Suite | **1618** green |
| `Analyze_ProducesTransportMetadata` | ✅ |
| `Analyze_ProducesStorageMappingMetadata` | ✅ |
| MCP structured facts smoke | ✅ roots, hasStorage, hasTransport |
| Create + authoring context | ✅ |
| Pipeline Storage+Transport | ✅ |
| EnumSubset deleted | ✅ |

### Gaps — reopen

| ID | Issue |
|----|--------|
| **D3.6** | Pack test asserts **same** storage entity count with SqlPack vs without — **not** type-map variance. Exact Steps required differing SQL/types under different maps. |
| **D3.7 / D4.4** | Working tree still **dirty/uncommitted**. Hard rule: no Complete on dirty tree. |
| Optional | Fail-closed message still “Infrastructure pipeline…” |

### Residual order

```text
D3.6 real pack-variance → D3.7 → D4.4
```

See [`simple-agent-tasks/dau-README.md`](simple-agent-tasks/dau-README.md).


---

## 16. Review — fourth “suite Complete” claim (2026-07-25)

**Claim:** §15 pack-variance fixed; Complete.  
**Verdict:** **Accept product Complete.** Pack-variance test is real (`varchar` vs `TEXT`). Suite **1618** green. All prior product reopens closed.

**Ops residual only:** working tree still dirty — hard rule blocks “ship Complete” until commit (user must request) or explicit waive. Do not re-open product tasks.

See [`simple-agent-tasks/dau-README.md`](simple-agent-tasks/dau-README.md).
