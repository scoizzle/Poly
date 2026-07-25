# Analysis Pipeline Merge — Proposal

**Date:** 2026-07-24  
**Status:** Active — implement via simple-agent suite  
**Micro-tasks:** [`simple-agent-tasks/apm-README.md`](simple-agent-tasks/apm-README.md)  
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
CURRENT: APM.A1 — metadata bridge
THEN:    A2 → A3 → A4 → A5 → Gate (Phase A)
PULL:    Phase B (B1–B3); CrossReference consumer; Transport keep/drop
```
