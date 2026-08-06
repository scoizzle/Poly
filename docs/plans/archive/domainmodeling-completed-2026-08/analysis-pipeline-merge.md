# Analysis Pipeline Merge — Proposal

**Date:** 2026-07-24  
**Status:** ✅ **Complete** — registration of topo/agg/beh/crossref + diagnostics. Design reference only.  
**Successor:** [`domain-analysis-unification.md`](domain-analysis-unification.md) · [`dau-*`](simple-agent-tasks/dau-README.md) (finish ownership, unify walks, storage/transport in Analysis).  
**Micro-tasks:** [`simple-agent-tasks/apm-README.md`](simple-agent-tasks/apm-README.md) (closed)  
**Related:** [`docs/domainmodeling-capability-inventory.md`](../domainmodeling-capability-inventory.md) · archived infra suite [`archive/infrastructure-pass/README.md`](archive/infrastructure-pass/README.md) · [`docs/CORE.md`](../CORE.md)

---

## 1. Problem

DomainModeling runs **two analysis pipelines** today:

| Pipeline | Passes | When | Entry |
|----------|--------|------|--------|
| **Domain** | 17 analyzers (structure → semantics → policy → effects → subscriptions → entity syntax) | Every `DomainEvolution.Apply()`, MCP `apply_dsl` / `get_domain_analysis` | `DomainModelAnalyzer` → `UseDomainModelAnalysisPipeline()` |
| **Infra (codegen)** | 5 passes: topology, aggregate, behavior, storage, transport (+ pack registry) | Only `DslCompiler.GenerateAllFiles` | Inline `AnalyzerBuilder` in `src/Poly.DslCompiler/DslCompiler.cs` |

Verified registration (domain) — `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs`:

```text
Structural → Semantic → PolicyConstraint → Effect → ConstraintQuality → EffectOrdering
→ EnumSubset → Capability → ConstraintPropagation → RuleCoverage → ContractIntegration
→ ActionParameterUsage → EntityStructure → Subscription{Contract,Causality,ReplaySafety}
→ AuthoringSuggestion → EntitySyntax
```

Verified registration (codegen) — `DslCompiler.GenerateAllFiles` ~196–215:

```text
EffectTopologyPass
OwnershipAggregatePass(priorDomainAnalysis)
BehaviorPass(priorDomainAnalysis)
StoragePass(typeMaps, conventions, priorDomainAnalysis)
TransportPass
+ authoring.Passes.Build()
→ Analyze(domain, priorAnalysis: domainAnalysis, invalidatedNodes: [domain])
```

### Consequences

1. **Late domain facts.** Aggregate hierarchy, effect topology, and behavior models exist only at codegen time. Agents using `apply_dsl` / `get_domain_analysis` never see them (or diagnostics derived from them).
2. **Re-walk.** Codegen re-analyzes the domain (with `priorAnalysis`) instead of reusing domain-pipeline metadata for pure domain facts.
3. **Siloed `AnalysisResult`.** Topology / aggregate / behavior live on `infraResult`; entity syntax lives on `analysis`. Generators need both; fail-closed checks today look only at `infraResult`.
4. **Missed early warnings.** Orphan aggregates, root conflicts, and topology smells are invisible until `--mode db|all`.

This is **not** “delete the infra pipeline.” Storage and transport stay codegen-gated (authoring type maps / packs).

---

## 2. Domain fact vs codegen-specific

| Pass | Pure domain fact? | Needs packs / type maps? | Target home |
|------|-------------------|--------------------------|-------------|
| `EffectTopologyPass` | ✅ create-in / cross-invoke / subscription coupling | No | **Domain pipeline** |
| `OwnershipAggregatePass` | ✅ root/child hierarchy | No (uses topology + `EntityStructureMetadata`) | **Domain pipeline** |
| `BehaviorPass` | ✅ actions, params, effective policies, transitions | No (uses semantic/capability metadata) | **Domain pipeline** |
| `StoragePass` | ❌ columns, SQL types, tables, FKs | **Yes** — `TypeMappingRegistry`, conventions | **Codegen pipeline** |
| `TransportPass` | ❌ API routing / exposability | Protocol-specific; currently unused consumer | **Codegen pipeline** (or drop — G6.h1) |
| `CrossReferencePass` | ✅ entity dependency graph + cycles | No | **Pull** — already exists, deferred (no consumer) |

**Important:** Topology / aggregate / behavior wrappers live under `Analysis/`; heavy lifting stays in `Lowering/*Analyzer`. Moving is primarily **registration + metadata bridging**, not new algorithms.

---

## 3. Design: two tiers after merge

```text
                    DomainEvolution / MCP
                              │
                              ▼
              UseDomainModelAnalysisPipeline()
         (existing 17 + topology + aggregate + behavior)
                              │
                              ▼
                     AnalysisResult (domain)
                    metadata for generators:
                    EntitySyntax, Topology,
                    Aggregate, Behavior, …
                              │
              ┌───────────────┴───────────────┐
              │ entities-only                 │ db / all
              ▼                               ▼
         emit entity C#              Codegen pipeline only:
                                     StoragePass (+ packs)
                                     TransportPass (optional)
                                     priorAnalysis: domain result
                                              │
                                              ▼
                                     DbContext / Program / .http
```

### 3.1 Domain pipeline registration (after)

Register **after** producers that Aggregate/Behavior consume:

| Pass | Must run after |
|------|----------------|
| `EffectTopologyPass` | Structural/semantic domain OK; no hard dep (empty `Dependencies`) |
| `OwnershipAggregatePass` | `EntityStructureAnalyzer` (root flags), `EffectTopologyPass`, `SemanticDomainAnalyzer` (type lookup) |
| `BehaviorPass` | `SemanticDomainAnalyzer` (effective policies, type lookup), `CapabilityAnalyzer` (transition targets) |

Suggested order (tail of pipeline):

```csharp
// … existing through SubscriptionReplaySafetyAnalyzer …

builder.AddAnalyzer(new EffectTopologyPass());
builder.AddAnalyzer(new OwnershipAggregatePass()); // no frozen AnalysisResult ctor
builder.AddAnalyzer(new BehaviorPass());

builder.AddAnalyzer(new AuthoringSuggestionAnalyzer());
builder.AddAnalyzer(new EntitySyntaxPass());
```

Declare explicit `Dependencies` on the pass types (framework orders by id):

```csharp
// OwnershipAggregatePass
public string[] Dependencies => [
    EffectTopologyPass.Id,
    EntityStructureAnalyzer.Id,      // "DomainEntityStructureAnalyzer"
];

// BehaviorPass
public string[] Dependencies => [
    SemanticDomainAnalyzer.Id,       // "DomainSemanticDomainAnalyzer"
    CapabilityAnalyzer.Id,           // "DomainCapabilityAnalyzer"
];
```

(`EffectTopologyPass` stays `Dependencies => []`.)

### 3.2 Codegen pipeline (after)

```csharp
// Domain analysis already completed (passed in as `analysis`)
var behaviorModel = analysis.GetMetadata<BehaviorMetadata>(domain)?.Behavior;
var aggregateModel = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate;

// Storage (+ optional Transport + pack passes) only
var codegenBuilder = new AnalyzerBuilder()
    .AddAnalyzer(new StoragePass(
        typeMaps: authoring?.TypeMaps,
        conventions: authoring?.StorageConventions,
        analysis: analysis)); // prior domain metadata for type lookup / aggregate-aware storage

if (/* keep TransportPass until G6.h1 */)
    codegenBuilder.AddAnalyzer(new TransportPass());

if (authoring is not null)
    foreach (var pass in authoring.Passes.Build())
        codegenBuilder.AddAnalyzer(pass);

var codegenResult = codegenBuilder.Build()
    .Analyze(domain, priorAnalysis: analysis, invalidatedNodes: [domain]);

var storageModel = codegenResult.GetMetadata<StorageMappingMetadata>(domain)?.Storage;
// Fail-closed: storage for db|all; behavior+aggregate for all — from `analysis`, not storage-only result
```

**Do not** re-extract behavior/aggregate from `codegenResult` after the move — they live on `analysis`.

---

## 4. Critical implementation detail: metadata bridging

Today `OwnershipAggregatePass` / `BehaviorPass` pass a frozen `AnalysisResult?` into `AggregateAnalyzer` / `BehaviorAnalyzer`, which call:

| Consumer | Metadata needed |
|----------|-----------------|
| `AggregateAnalyzer` | `DomainTypeLookupMetadata`, `EntityStructureMetadata` per entity |
| `BehaviorAnalyzer` | `DomainTypeLookupMetadata`, `EffectivePoliciesMetadata`, `ActionCapabilityMetadata`, `ResolvedTypeReferenceMetadata` |

In the **codegen** path, `priorAnalysis: domainAnalysis` already put those facts on the context; the ctor still injects the completed domain `AnalysisResult` for analyzer helpers.

If we only delete the ctor and pass `analysis: null`, analyzers **fall back** to weaker heuristics (e.g. root detection via required entity refs only) — a silent regression.

### Required bridge (smallest correct fix)

Pass **live context metadata**, not a second frozen result:

**Option A (preferred):** teach analyzers to accept `AnalysisContext` (or a tiny `IMetadataLookup` facade):

```csharp
// AggregateAnalyzer
public AggregateAnalyzer(Domain domain, AnalysisContext? context = null) { … }

private bool IsRootEntity(Entity entity) {
    var meta = _context?.GetMetadata<EntityStructureMetadata>(entity)
        ?? _analysis?.GetMetadata<EntityStructureMetadata>(entity);
    …
}
```

**Option B:** keep `AnalysisResult?` only for external callers/tests; in-pipeline passes construct nothing — pass `context` into `Analyze(topology, context)`.

**Out of scope for a wrong “tiny” PR:** moving the three passes without this bridge.

Same for `StoragePass`: it already takes `analysis` for type lookup; after merge, continue passing the **domain** `AnalysisResult` (now richer with topology/aggregate on the same object once those passes run domain-side). During codegen, `priorAnalysis` + StoragePass ctor `analysis` should be the same domain result.

---

## 5. Phased delivery

**Executable queue:** [`simple-agent-tasks/apm-README.md`](simple-agent-tasks/apm-README.md) — pick first `[ ]` there.

### Phase A — Merge registration (must ship first)

| # | Task file | Notes |
|---|-----------|--------|
| **A1** | [`apm-a1-metadata-bridge.md`](simple-agent-tasks/apm-a1-metadata-bridge.md) | Aggregate/Behavior read `AnalysisContext` |
| **A2** | [`apm-a2-register-domain-pipeline.md`](simple-agent-tasks/apm-a2-register-domain-pipeline.md) | Register 3 passes + Dependencies |
| **A3** | [`apm-a3-dslcompiler-slim.md`](simple-agent-tasks/apm-a3-dslcompiler-slim.md) | Storage-only codegen pipeline |
| **A4** | [`apm-a4-domain-metadata-tests.md`](simple-agent-tasks/apm-a4-domain-metadata-tests.md) | Domain analysis metadata tests |
| **A5** | [`apm-a5-codegen-regression.md`](simple-agent-tasks/apm-a5-codegen-regression.md) | AllMode + generator suites |
| **Gate** | [`apm-gate-phase-a.md`](simple-agent-tasks/apm-gate-phase-a.md) | Pre-ship review |

**Exit A:** Codegen works; domain analysis carries the three metadata bags; no new diagnostic codes required.

### Phase B — Diagnostics (optional, separate PR)

| # | Task file | Codes / focus |
|---|-----------|----------------|
| **B1** | [`apm-b1-aggregate-diagnostics.md`](simple-agent-tasks/apm-b1-aggregate-diagnostics.md) | DMAGG001 / DMAGG002 |
| **B2** | [`apm-b2-cycle-diagnostics.md`](simple-agent-tasks/apm-b2-cycle-diagnostics.md) | Prefer CrossReferencePass over new DMEFF010 |
| **B3** | [`apm-b3-behavior-hint.md`](simple-agent-tasks/apm-b3-behavior-hint.md) | DMBEH001 as hint/suggestion only |

| Code | Severity | Condition | Caution |
|------|----------|-----------|---------|
| **DMAGG001** | Warning | Non-root with no aggregate parent | Partial models while authoring |
| **DMAGG002** | Warning | Structural root ≠ aggregate root | Good early signal |
| **Cycle** | Warning | Dependency cycle | One story only — CrossReferencePass preferred |
| **DMBEH001** | Hint | Action no requires + no params | Noisy — never Error |

**Exit B:** MCP/analysis shows codes on fixtures; suite green; dogfood check for noise.

### Phase C — Pull (not this suite)

- `CrossReferencePass` consumer beyond B2  
- `TransportPass` keep-or-drop (G6.h1)  
- Pack domain-level passes if ever needed

---

## 6. What stays in DslCompiler

| Component | Reason |
|-----------|--------|
| `StoragePass` | SQL types, tables, keys, FKs — pack/type-map dependent |
| `TransportPass` | Protocol surface; no production consumer yet |
| `PassRegistry` / pack analyzers | Storage annotation enrichment |
| Entity file split + IR emit | Post-analysis formatting |

---

## 7. What does not change

- Pass types remain under `Poly/DomainModeling/Analysis/`
- `Lowering/*Analyzer` remain implementation helpers
- Metadata record types unchanged (`IAnalysisMetadata`)
- MCP tools automatically see domain-pipeline diagnostics/metadata once Phase A lands
- HttpFile string emit remains intentional
- Infra **Bar B** / RestApiSurfacePass stay pull (infra archive)

---

## 8. Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj

# Domain analysis metadata smoke (new test preferred)
# DomainModelAnalyzer.Analyze(domain) has Topology + Aggregate + Behavior metadata

# Codegen smoke
dotnet run --project src/Poly.DslCompiler/Poly.DslCompiler.csproj -- \
  --mode all --dbms sqlite path/to/library.poly /tmp/merge-smoke
# Expect: {Domain}DbContext.cs + Program.cs + demo.http (not LibraryDbContext hard-code)
```

Optional: structural IR tests (`DbContextGeneratorTests`, `MinimalApiGeneratorTests`, AllMode) stay green.

---

## 9. Risks & mitigations

| Risk | Mitigation |
|------|------------|
| Silent root/policy regression if `_analysis` dropped | **Phase A1 mandatory** — bridge context metadata |
| Evolution cost: 3 more passes on every Apply | Passes already skip on `HasStructuralFailure`; measure if needed; all are domain-root scoped |
| Diagnostic noise (Phase B) | Warnings/hints only; DMBEH001 in suggestions, not hard fail |
| Dependency order bugs | Explicit `Dependencies[]` + tests for metadata presence |
| Pack passes expecting topology only at codegen | Still available on `priorAnalysis` when Storage runs |
| Duplicate cycle diagnostics | Prefer one cycle story (`CrossReferencePass` vs new DMEFF010) |

---

## 10. Non-goals

- Merging Storage into always-on domain analysis (would need pack maps on every evolve)
- Bar B string-oracle parity
- New RestApiSurfacePass
- Reopening Q3′ / link_instances product work
- Changing VM / DE lowering

---

## 11. Recommended first PR

**Phase A only** via suite **A1→Gate**: metadata bridge + register three passes + slim DslCompiler + tests.  
No new diagnostic codes. Smallest coherent customer win: domain analysis and codegen share topology/aggregate/behavior facts.

---

## 12. Agent pick

**Micro-tasks:** [`simple-agent-tasks/apm-README.md`](simple-agent-tasks/apm-README.md)

```text
DONE:    APM suite complete (A–E′ residuals closed). 1611 green.
CURRENT: Post-suite — next product work
PULL:    Transport keep/drop; optional heuristic-root test
```

---

## 13. Plan review — Phase A implementation (uncommitted, 2026-07-24)

**Scope**

| Area | Change |
|------|--------|
| Bridge | `AggregateAnalyzer` / `BehaviorAnalyzer` take optional `AnalysisContext`; context-first metadata |
| Domain pipeline | Register `EffectTopologyPass`, `OwnershipAggregatePass`, `BehaviorPass` |
| Codegen | DslCompiler drops those three; behavior/aggregate from domain `analysis` |
| Storage/Transport | `Dependencies => []` so codegen pipeline does not require domain-only pass ids |
| Tests | `PipelineMergeMetadataTests` (4) — topology / aggregate roots / behavior action / all three |

**Re-verified:** PipelineMerge **4/4**; AllMode **1/1**; DbContext **11/11**; MinimalApi **24/24**; DslCompiler build clean.

**Verdict:** **Phase A product bar met.** Direction matches the proposal. Ship after addressing or explicitly accepting **A′.1** (Dependencies). No 🔴. Uncommitted — Gate should not claim Done until commit.

### Solid

| Item | Notes |
|------|--------|
| Context-first bridge | `GetMetadata` from context then `AnalysisResult` — correct |
| DslCompiler slim | Topology/aggregate/behavior only from domain analysis; fail-closed messages updated |
| Storage deps cleared | Required: `AnalyzerBuilder` throws if Dependencies name unregistered passes |
| A4 tests | Patron root / Loan child; Activate on Behavior; single Analyze has all three |
| No Phase B codes | Correct for Phase A |
| Registration placement | After subscriptions, before AuthoringSuggestion / EntitySyntax |

### Findings → follow-up tasks

| ID | Sev | Finding | Fix |
|----|-----|---------|-----|
| **A′.1** | **Med (fragility)** | `OwnershipAggregatePass.Dependencies` is only `[EffectTopologyPass.Id]` — not `EntityStructureAnalyzer.Id`. `BehaviorPass.Dependencies` is still `[]` — not Semantic/Capability. Order works only because of **registration order** after EntityStructure. Reordering `UseDomainModelAnalysisPipeline` can silently regress roots/policies. | Declare Dependencies per §3.1 (`EntityStructureAnalyzer.Id`, `SemanticDomainAnalyzer.Id`, `CapabilityAnalyzer.Id`). |
| **A′.2** | Low | A1 asked for an explicit context-vs-heuristic root unit test; suite only proves happy-path metadata. | Optional: fixture where structure metadata IsRoot differs from heuristic. |
| **A′.3** | Low | Pass ctors still take `AnalysisResult? _analysis` always null from domain registration. | Remove dead ctor param after no remaining callers, or keep for tests. |
| **A′.4** | Low | `TransportPass` error text still says “run … before TransportPass” — they now run in **prior** domain analysis. | Update message to “ensure domain pipeline produced … / priorAnalysis”. |
| **A′.5** | **Ops** | apm-README marks Gate `[x]` while tree dirty. | Commit Phase A; then flip pick to Phase B / post-suite. |
| **A′.6** | Hygiene | Inventory §5 still says topology/aggregate/behavior are codegen-only. | Update after commit. |
| **B\*** | Pull | Phase B diagnostics | Unchanged |

### Three-layer

| Concern | Status |
|---------|--------|
| Metadata on domain evolve | ✅ tests |
| Codegen fail-closed | ✅ still throws; messages point at domain pipeline |
| Silent heuristic root | 🟡 mitigated by registration order + context; **A′.1** locks order |

### Follow-up checklist (historical §13)

- [~] **A′.1** Partial: Behavior depends on OwnershipAggregate only; Aggregate still topology-only — see **§14 B′.4**  
- [ ] **A′.2** (optional) Context-vs-heuristic root test  
- [ ] **A′.3** (optional) Drop dead `AnalysisResult?` pass ctors  
- [x] **A′.4** TransportPass message mentions priorAnalysis / domain pipeline  
- [ ] **A′.5** **Commit** — still open  
- [ ] **A′.6** Inventory §5 after commit  

---

## 14. Plan review — Phase A+B tree (uncommitted, 2026-07-24)

**Scope (beyond §13)**

| Area | Change |
|------|--------|
| Phase A | Bridge + domain registration + DslCompiler slim + `PipelineMergeMetadataTests` (4) |
| B1 | `OwnershipAggregatePass` emits **DMAGG001** / **DMAGG002** warnings |
| B2 | `CrossReferencePass` registered on domain pipeline; cycle code → **DMDEP001** |
| B3 | `AuthoringSuggestionAnalyzer.SuggestUnconditionalActions` → **DMBEH001** hints |
| Codes | `DomainModelDiagnosticCodes` additions |

**Re-verified:** PipelineMerge **4/4**; AllMode **1/1**; build clean. **No new tests** for DMAGG/DMDEP/DMBEH.

**Verdict:** **Phase A still shippable.** Phase B is **implementation without proof** and has at least one likely-dead diagnostic (**DMAGG002**). Do **not** mark complete / Gate green until **B′.1** (diagnostic tests) lands or Phase B is split out of the commit. Prefer: commit Phase A alone **or** add B diagnostic goldens before one combined commit.

### Solid

| Item | Notes |
|------|--------|
| Phase A merge | Matches proposal; metadata on domain analysis; codegen slim |
| Context-first bridge | Correct |
| B2 wiring | CrossReferencePass after topology/aggregate; stable **DMDEP001** |
| B3 placement | Hint on AuthoringSuggestionAnalyzer — never blocks evolution |
| Transport message | Updated for priorAnalysis story |
| Fail-closed codegen | Still throws; messages point at domain pipeline |

### Findings → follow-up tasks

| ID | Sev | Finding | Fix |
|----|-----|---------|-----|
| **B′.1** | **High (contract)** | Phase B B1–B3 marked `[x]` with **zero diagnostic fixture tests**. Exit B required crafted fixtures. | Add tests: (1) orphan → DMAGG001; (2) cycle → DMDEP001; (3) bare action → DMBEH001; (4) guarded action → no DMBEH001. |
| **B′.2** | **High (honesty)** | **DMAGG002 likely never fires** when `EntityStructureAnalyzer` ran: `AggregateAnalyzer.IsRootEntity` sets aggregate root **from** `EntityStructureMetadata.IsRoot`, so struct root always equals aggregate root. | Prove with a test **or** remove/rewrite DMAGG002 to a real conflict (different signals). Do not ship a dead code path as “done.” |
| **B′.3** | **Med (noise)** | **DMBEH001** fires on every parameterless, unguarded action (entity + stage), including common demos (`Activate`, seed-style actions). Will flood `get_domain_suggestions` / analysis. | Narrow (e.g. only when entity has policies elsewhere, or stage-gated workflows exist); or document expected volume; dogfood check. |
| **B′.4** | Med | **A′.1 incomplete:** `OwnershipAggregatePass.Dependencies` still only Topology; `BehaviorPass` only OwnershipAggregate — not EntityStructure / Semantic / Capability. | Declare full Dependencies per §3.1. |
| **B′.5** | Low | BehaviorPass depends on OwnershipAggregate though BehaviorAnalyzer does not use aggregate — artificial coupling. | Prefer Semantic + Capability deps only. |
| **B′.6** | Ops | Plan header said “Complete / 1602 green” while uncommitted + Phase B untested. | Honest status; commit after B′.1 or split A vs B commits. |
| **B′.7** | Hygiene | Inventory still describes topology/aggregate/behavior as codegen-only. | Update §5 after ship. |
| **B′.8** | Low | Dead `AnalysisResult?` pass ctors still unused. | Optional remove. |

### Three-layer (Phase B)

| Code | Emit | Test | Noise / reachability |
|------|------|------|----------------------|
| DMAGG001 | Warning on orphan non-root | ❌ none | Plausible |
| DMAGG002 | Warning on root conflict | ❌ none | **Likely unreachable** |
| DMDEP001 | Warning on cycle | ❌ none | CrossReference wired ✅ |
| DMBEH001 | Hint unconditional action | ❌ none | **High volume** |

### Follow-up checklist (historical §14)

- [x] **B′.1** Diagnostic goldens (7 fixtures in `PipelineMergeMetadataTests`)  
- [x] **B′.2** DMAGG002 removed (dead — see codes comment)  
- [x] **B′.3** DMBEH001 gated on `hasPoliciesElsewhere` + negative tests  
- [ ] **B′.4** Full Dependencies on Aggregate/Behavior — still open → **§15 C′.1**  
- [ ] **B′.5** (optional) Drop Behavior→OwnershipAggregate dep → **§15 C′.1**  
- [ ] **B′.6** **Commit** — still open → **§15 C′.0**  
- [ ] **B′.7** Inventory §5 after commit → **§15 C′.2**  
- [ ] **B′.8** (optional) Dead pass ctors → **§15 C′.3**  

**§14 recommended next was:** B′.1 + B′.2 → B′.4 → commit. **B′.1–B′.3 done; see §15.**

---

## 15. Plan review — post B′.1–B′.3 tree (uncommitted, 2026-07-24)

**Scope (delta since §14)**

| Area | Change |
|------|--------|
| B′.1 | 7 diagnostic fixtures: DMAGG001 ±, DMDEP001 ±, DMBEH001 + guard − + no-policy − |
| B′.2 | **DMAGG002 removed** from `OwnershipAggregatePass` + codes comment |
| B′.3 | `SuggestUnconditionalActions` only when entity has policies elsewhere |
| Suite | Full **1609** green (includes 11 PipelineMerge tests) |

**Re-verified this review**

| Check | Result |
|-------|--------|
| `dotnet build` | Clean |
| `PipelineMergeMetadataTests` / `DomainAnalysis_*` | Green |
| Full suite | **1609 / 1609** |
| `DslCompiler_AllMode_EmitsDbContextAndProgramViaIr` | Green |

**Verdict:** **Phase A + B product bar met.** No 🔴 Structure. No 🟠 Contract left unaddressed for the shipped diagnostic surface. **Not “suite Complete” in the commit sense** until **C′.0** lands — tree is still dirty; docs that say “Complete / post-suite next product” overclaim. Prefer one combined A+B commit of product + tests + plan honesty.

### Solid

| Item | Notes |
|------|--------|
| Context-first bridge | Aggregate/Behavior: context then frozen `AnalysisResult` |
| Domain registration | Topology → Aggregate → Behavior → CrossReference → Authoring → EntitySyntax |
| Codegen slim | Storage(+Transport/packs) only; behavior/aggregate from domain `analysis`; fail-closed messages correct |
| Storage/Transport deps | `[]` — required so codegen `AnalyzerBuilder` does not demand domain-only pass ids |
| DMAGG001 | Orphan non-root with no parent; positive + hierarchy negative |
| DMDEP001 | Stable code; CrossReference wired on domain pipeline |
| DMBEH001 | Hint only; noise narrowed; three fixtures |
| DMAGG002 honesty | Removed rather than shipping dead path |

### Findings → follow-up tasks

| ID | Sev | Finding | Fix |
|----|-----|---------|-----|
| **C′.0** | **Ops** | Entire APM product + tests + plans **uncommitted**. README/header claim “Complete” while dirty. | Commit product + `PipelineMergeMetadataTests` + plan/README/roadmap updates. Gate “Done” only after clean tree. |
| **C′.1** | Med (fragility) | **B′.4 still open.** `OwnershipAggregatePass.Dependencies` = topology only (not `EntityStructureAnalyzer.Id`). `BehaviorPass.Dependencies` = `OwnershipAggregatePass` only (not Semantic/Capability). Order is registration-order luck. Behavior’s Aggregate dep is artificial (BehaviorAnalyzer never reads aggregate). | Declare: Aggregate → `[EffectTopologyPass.Id, EntityStructureAnalyzer.Id]`; Behavior → `[SemanticDomainAnalyzer.Id, CapabilityAnalyzer.Id]` (drop Aggregate). |
| **C′.2** | Hygiene | Inventory still says topology/aggregate/behavior are **codegen-only**. | Update [`domainmodeling-capability-inventory.md`](../domainmodeling-capability-inventory.md) §5 after or with commit. |
| **C′.3** | Low | Pass ctors still take unused `AnalysisResult?` for domain registration path (`null`). Still used by `InfrastructurePipelineTests` frozen-result path. | Keep until those tests migrate to context-only; then drop optional. |
| **C′.4** | Low (noise) | **DMDEP001** fires on normal bidirectional relationship pairs (e.g. `Patron.loans` + `Loan.borrower`), not only “bad” cycles. Negative fixture is single isolated entity — weak. | Pull: refine cycle story (exclude pure inverse pairs?) or document expected warning; stronger negative = one-way only. |
| **C′.5** | Low | Stage-level DMBEH001 path has no dedicated golden (entity-level covered). | Optional fixture if stage noise appears in dogfood. |
| **C′.6** | Low | `DomainModelDiagnosticCodes.cs` missing trailing newline. | Add EOF newline. |
| **C′.7** | Low (optional) | A′.2 still open: no explicit context-vs-heuristic root unit test. | Optional only. |

### Three-layer (current)

| Code | Emit | Test | Notes |
|------|------|------|-------|
| DMAGG001 | Warning orphan | ✅ ± | Reachable |
| DMAGG002 | — | — | Removed |
| DMDEP001 | Warning cycle | ✅ + weak − | Bidir noise **C′.4** |
| DMBEH001 | Hint unguarded | ✅ +/− | Narrowed **B′.3** |
| Metadata (topo/agg/beh) | Domain pipeline | ✅ 4 | Phase A |
| Codegen fail-closed | Throws if missing | ✅ AllMode | Domain analysis messages |

### Severity summary

| Sev | Count | Ship-blocking? |
|-----|-------|----------------|
| 🔴 Structure | 0 | — |
| 🟠 Contract | 0 | — |
| 🟡 Edge / fragility | **C′.4** | No — documented known behavior; pull refinement |
| ⚪ Hygiene / ops | **C′.3**, **C′.5**, **C′.7** | Pull / optional |

### Follow-up checklist (historical §15)

- [x] **C′.0** A+B product committed as `cc0ccef`  
- [x] **C′.1** Full Dependencies implemented in working tree (uncommitted) — see **§16**  
- [~] **C′.2** Inventory §5.1 updated; **§5.2/§5.3/§10 leftovers** → **§16 D′.2**  
- [ ] **C′.3** (keep / pull) Dead `AnalysisResult?` ctor while frozen-result tests exist  
- [ ] **C′.4** (pull) DMDEP001 bidir noise — comment landed → **§16**  
- [ ] **C′.5** (optional) Stage DMBEH001 fixture  
- [x] **C′.6** EOF newline (uncommitted with residual)  
- [ ] **C′.7** (optional) Context-vs-heuristic root test  
- [x] **B′.6** Infra pipeline test registers Semantic/EntityStructure/Capability for dep order  

**§15 recommended next was commit C′.1.** Residual still dirty — **§16**.

---

## 16. Plan review — residual C′ delta (uncommitted, 2026-07-24)

**Base:** `cc0ccef` (APM Phase A+B product + tests + initial plans).  
**Dirty residual (8 files):** Dependencies, inventory partial sync, CrossReference C′.4 note, EOF newline, infra test deps, plan/README picks.

| Area | Change |
|------|--------|
| **C′.1** | `OwnershipAggregatePass` → `[EffectTopologyPass, EntityStructureAnalyzer]`; `BehaviorPass` → `[SemanticDomainAnalyzer, CapabilityAnalyzer]` (dropped Aggregate) |
| **B′.6** | `Pipeline_Produces_StorageBehaviorAndAggregateMetadata` registers Semantic + EntityStructure + Capability so `AnalyzerBuilder` accepts new Dependencies |
| **C′.4** | Comment on CrossReference cycle detection (bidir pairs are honest graph cycles) |
| **C′.6** | Trailing newline on `DomainModelDiagnosticCodes.cs` |
| **C′.2 partial** | Inventory §5.1 lists topo/agg/beh/CrossReference on domain pipeline; §5.2 drops three codegen-only rows |

**Re-verified this review**

| Check | Result |
|-------|--------|
| `dotnet build` | Clean |
| `DomainAnalysis_*` / PipelineMerge | 13 green |
| `Pipeline_Produces_StorageBehaviorAndAggregateMetadata` | Green |
| `DslCompiler_AllMode_*` | Green |
| Full suite | **1609 / 1609** |

**Verdict:** Residual is **correct and shippable**. No 🔴/🟠. **Do not mark suite “Complete” in docs until this residual is committed** — product is already on `cc0ccef`; honesty gap is residual-only.

### Solid

| Item | Notes |
|------|--------|
| Dependencies match §3.1 | Aggregate needs structure metadata; Behavior needs semantic + capability |
| No artificial Behavior→Aggregate | Matches B′.5 |
| Infra test fix | Required: builder throws if deps unregistered |
| C′.4 documented | Comment is enough for pull; no behavior change |
| Suite green with residual | Safe to commit residual as follow-up commit |

### Findings → follow-up tasks

| ID | Sev | Finding | Fix |
|----|-----|---------|-----|
| **D′.0** | **Ops** | Residual C′ still **uncommitted** while header/apm-README say “Complete / post-suite”. | Commit residual (deps + inventory + tests + plans). Then true post-suite. |
| **D′.1** | Low (hygiene) | Pass XML docs stale: `BehaviorPass` says “No dependencies on other infra passes”; `OwnershipAggregatePass` only mentions EffectTopology. | Update summaries to match `Dependencies`. |
| **D′.2** | Hygiene | Inventory leftovers after partial C′.2: (1) §5.2 still lists `CrossReferencePass` under **codegen** table; (2) §5.3 still “Open design work: unify…”; (3) §10 row “CrossReferencePass wiring … Deferred” contradicts §5.1. | Drop CrossReference from §5.2 table; close §5.3 open-work line; mark §10 CrossReference ✅ Done / remove row. |
| **D′.3** | Low | `InfrastructurePipelineTests` still re-runs Topology/Aggregate/Behavior in a mini pipeline — **not** production codegen shape (storage-only). Useful for dep smoke; diverges from real path. | Optional: slim test to Storage(+Transport) + `priorAnalysis: domainResult` only, assert metadata already on domain result. |
| **D′.4** | Pull | **C′.4** bidir DMDEP001 noise — documented, not refined. | Dogfood; exclude pure inverse pairs only with a test. |
| **D′.5** | Pull | **C′.3** frozen `AnalysisResult?` pass ctors. | Keep while infra tests pass frozen result. |
| **D′.6** | Optional | **C′.5** stage DMBEH001; **C′.7** context-vs-heuristic root. | Only if dogfood wants them. |
| **D′.7** | Hygiene | Plan §12 agent pick + plans README + master-roadmap still lag residual. | Sync on D′.0 commit. |

### Three-layer

| Concern | Status |
|---------|--------|
| Dep order locked by framework | ✅ C′.1 + infra test |
| Diagnostics (B) | ✅ fixtures |
| Inventory product claim | ✅ **D′.2** synced |
| Infrastructure test | ✅ **D′.3** slimmed to production shape |
| Plan/README picks | ✅ **D′.7** synced |
| Residual committed | ❌ **D′.0** |

### Severity summary

| Sev | Count | Ship-blocking? |
|-----|-------|----------------|
| 🔴 Structure | 0 | — |
| 🟠 Contract | 0 | — |
| 🟡 | 0 new product | — |
| ⚪ Ops / hygiene | **D′.4–D′.6** | Pull / optional — suite closed |

### Follow-up checklist (historical §16)

- [ ] **D′.0** **Commit residual** — product done in tree, **not** committed → **§17 E′.0**  
- [x] **D′.1** XML doc sync on Aggregate/Behavior passes  
- [x] **D′.2** Inventory §5.2 / §5.3 / §10 CrossReference honesty  
- [x] **D′.3** `InfrastructurePipelineTests` slimmed to storage-only codegen shape  
- [ ] **D′.4** (pull) DMDEP001 bidir refinement  
- [ ] **D′.5** (pull) Drop frozen pass ctors when safe  
- [ ] **D′.6** (optional) Stage DMBEH + heuristic-root tests  
- [~] **D′.7** Picks partially updated; header falsely claimed “suite closed” → **§17**  

**§16 product residual met; suite not closed until E′.0 commit. See §17.**

---

## 17. Plan review — D′ product residual (uncommitted, 2026-07-25)

**Base:** `cc0ccef` (APM A+B on origin).  
**Dirty (10 files):** C′.1 Dependencies + XML, inventory honesty, slim infra test, CrossReference C′.4 note, EOF newline, plan/README/roadmap picks.

| Area | Change vs `cc0ccef` |
|------|---------------------|
| **C′.1 / D′.1** | Aggregate deps → Topology + EntityStructure; Behavior → Semantic + Capability; XML matches |
| **D′.2** | Inventory §5.1 domain rows; §5.2 storage/transport/packs only; §5.3 open-work removed; §10 CrossReference ✅ |
| **D′.3** | `Pipeline_Produces_*` asserts topo/agg/beh on **domain** result; codegen builder = StoragePass only |
| **C′.4** | CrossReference cycle comment (bidir known noise) |
| **C′.6** | `DomainModelDiagnosticCodes` trailing newline |

**Re-verified this review**

| Check | Result |
|-------|--------|
| `dotnet build` | Clean |
| `DomainAnalysis_*` | 13 green |
| `Pipeline_Produces_StorageBehaviorAndAggregateMetadata` | Green (slim path) |
| `DslCompiler_AllMode_*` | Green |
| Full suite | **1609 / 1609** |

**Verdict:** **Product residual is done and correct.** No 🔴 Structure. No 🟠 Contract. The only ship-blocking gap is **ops honesty**: docs/checklist claimed “Complete / D′.0 done / suite closed” while the residual tree is still dirty. **Commit, then close.**

### Solid

| Item | Notes |
|------|--------|
| Dependencies | Match design §3.1; Behavior no longer couples to Aggregate |
| Inventory | Domain vs codegen split matches production |
| Infra test | Mirrors real DslCompiler shape (storage + prior domain analysis) |
| Domain metadata assertions | Still prove merge (topo/agg/beh on `DomainModelAnalyzer.Analyze`) |
| Diagnostics | Unchanged on `cc0ccef`; still covered by PipelineMerge fixtures |
| Suite | Full green with residual applied |

### Findings → follow-up tasks

| ID | Sev | Finding | Fix |
|----|-----|---------|-----|
| **E′.0** | **Ops** | Residual D′ still **uncommitted**. §16 checklist had D′.0 `[x]` and “suite closed” prematurely. | `git add` product + inventory + plans; commit; only then mark Complete / post-suite. |
| **E′.1** | Low | `InfrastructureAnalyzerTests.cs` missing trailing newline after slim edit. | Add EOF newline in residual commit. |
| **E′.2** | Low | Test name `Pipeline_Produces_StorageBehaviorAndAggregateMetadata` still OK (asserts all three + storage) but “Pipeline” now means domain+codegen hybrid. | Optional rename e.g. `DomainAnalysis_HasInfraMetadata_CodegenProducesStorage` — not required. |
| **E′.3** | Pull | **D′.4** DMDEP001 on intentional bidir navigations. | Dogfood / exclude pure inverses with a test. |
| **E′.4** | Pull | **D′.5** `AnalysisResult?` pass ctors unused on domain registration; analyzers still take frozen result for unit tests (`GenerationAssertions`, analyzer unit tests). | Keep; drop only when no callers pass non-null. |
| **E′.5** | Optional | **D′.6** stage DMBEH001 + context-vs-heuristic root. | Only if dogfood wants. |
| **E′.6** | Hygiene | §12 pick / plans README / master-roadmap lag or overclaim vs dirty tree. | Fixed with this §17 + E′.0 commit. |

### Three-layer

| Concern | Status |
|---------|--------|
| Dep order | ✅ framework + registration |
| Domain metadata | ✅ PipelineMerge + slim infra test |
| Codegen storage | ✅ StoragePass + priorAnalysis |
| Residual on origin | ❌ until **E′.0** |

### Severity summary

| Sev | Count | Ship-blocking? |
|-----|-------|----------------|
| 🔴 | 0 | — |
| 🟠 | 0 | — |
| 🟡 product | 0 | — |
| ⚪ Ops | **E′.0** | **Yes for “Done”** — not for product correctness |
| Pull | E′.3–E′.5 | No |

### Follow-up checklist (historical §17)

- [x] **E′.0** Residual committed with E′ product fixes  
- [x] **E′.1** EOF newline on `InfrastructureAnalyzerTests.cs`  
- [x] **E′.2** Renamed slim test → `DomainAnalysis_HasInfraMetadata_CodegenProducesStorage`  
- [x] **E′.3** DMDEP001 excludes pure inverse relationship pairs; 3-entity cycle + bidir negatives  
- [x] **E′.4** Dropped unused `AnalysisResult?` from OwnershipAggregatePass / BehaviorPass  
- [x] **E′.5** Stage DMBEH001 fixture added (heuristic-root still optional pull)  
- [x] **E′.6** Plan honesty  

**§17 residual closed in commit after this checklist.** Suite complete — see status header.
