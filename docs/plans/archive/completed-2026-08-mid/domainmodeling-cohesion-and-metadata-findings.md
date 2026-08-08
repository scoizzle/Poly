# DomainModeling — cohesion, experiment direction, and analysis-metadata findings

**Date:** 2026-08-06  
**Status:** Findings / orientation (not an admitted implementation suite)  
**Audience:** Humans and agents choosing the next CURRENT workstream  
**Related:**  
- [`domainmodeling-workstream-map.md`](domainmodeling-workstream-map.md)  
- [`v2-to-v3/master-roadmap.md`](v2-to-v3/master-roadmap.md) (Agent pick)  
- [`domain-dsl-absorption-proposals.md`](domain-dsl-absorption-proposals.md)  
- [`docs/experiments/DOMAIN-DSL-SPEC.md`](../experiments/DOMAIN-DSL-SPEC.md) (vision; not product truth)  
- Product DSL: `Poly.Mcp/Docs/poly-dsl-guide.md`  
- Mechanisms: [`docs/CORE.md`](../CORE.md)  
- Completed analysis work: [`archive/domainmodeling-completed-2026-08/`](archive/domainmodeling-completed-2026-08/README.md)

**Rule:** This document does **not** admit CURRENT. One primary suite only — update master-roadmap Agent pick when admitting work.

---

## 1. Snapshot

| Lens | State |
|------|--------|
| Product vertical (M1–M4, spawn-and-wire, Q1′/Q3′, link, SPE, DAS catalog monopath) | **Done** (suites archived) |
| Agent pick | **`dogfood-wave-2`** (S4→S5→S6) admitted 2026-08-06 |
| Dominant residual risks | Multi-stream thrash · under-used analysis bags · experiment vision vs product spine · structural monolith |

**Honest product claim (today):** Immutable domain → evolution + analysis gate → DE lower → Syntax → VM; MCP thin. Path-prefix / exists / where / Q3′ quantifiers evaluable store-linked; peer `when Rel Stage as name`; entity-level when; owned policies; catalog monopath for product name→member lookups. Temporal DSL authoring, actors, schedule, relationship-as-property rewrite are **not** product.

---

## 2. Completed vs live surface

### 2.1 Archived (do not re-execute)

`archive/domainmodeling-completed-2026-08/` holds finished suites and parents: `apm`, `das`, `dacr`, `dar`, `dau`, `spe`, `qe`, `vs`, plus quality/peer followups.

### 2.2 Live plans worth knowing

| Role | Plans |
|------|--------|
| Admission | `docs/plans/README.md`, master-roadmap Agent pick |
| Parked product | Dogfood protocol · DSL absorption P* · effect-surface residuals · MCP tool expansion · infra Bar B pull |
| Structural hygiene | Decomposition proposal · grammar-integration · analysis-consuming-lowering (partial residual) · naming prose |
| Guardrails | Capability inventory · abstraction gaps · anti-patterns |

---

## 3. Experiment DSL direction (re-evaluation)

Source: [`docs/experiments/DOMAIN-DSL-SPEC.md`](../experiments/DOMAIN-DSL-SPEC.md) (**vision**). Product truth remains the MCP guide + shipped code.

### 3.1 Where the experiment is headed

```text
Card editor / text / MCP / agent text
        → shared analyzer
        → .poly (VCS, diff, review)
        → committed domain IR
        → REST / gRPC / GraphQL / storage lowering
```

- **DSL** = human/LLM/diff primary artifact; **JSON/MCP** = machine secondary.  
- Stages as lifecycle graph; **transition is the observable** (events/`publish` rejected).  
- Rich Phase 2 surface: actors, values, `schedule at`, `for`/`parallel`, `when any/all`, external policies, domain kinds, library `import`.  
- Emergent claims: HATEOAS from stages, RBAC links from `require` + actor policies.

### 3.2 Stale experiment architecture (do not rebuild)

| Experiment text | Product spine |
|-----------------|---------------|
| Parse → `DomainMutationIntent[]` | `DomainEvolution` / `DomainChange` + analysis gate |
| Intent log as primary representation | Rejected for authoring; evolution is mutation path |
| Relationships only as property lines + implicit reverse | Named relationships + navs; MCP `link_instances` |

### 3.3 Product sequencing under experiment lens

```text
Dogfood current surface (trust)
  → temporal vertical (P1) when scenarios force or strategy prioritizes lifecycle language
  → when any/all (P4) + return-type honesty (P3)
  → host seams: actor principal, external policy, schedule adapter
  → multi-target lowering (OpenAPI/HATEOAS/RestApi) from domain IR
  → late: relationship-as-property rewrite, import packages, domain kinds
```

**Park:** intent-log pipeline, for/parallel completeness catalog, grammar re-base as prereq, multi-assembly without a real consumer.

**Implication:** “Not dates first” (2026-08-04 thrash control) was process discipline. Vision-wise temporal is **core lifecycle language**; still admit as **one** suite, not parallel with everything else.

---

## 4. Cohesion opportunities (implementation structure)

Deep research + live layout. Prefer **single-project tiered folders**; multi-assembly only with a real subset consumer.

### 4.1 Highest leverage (non-speculative)

| # | Opportunity | Notes |
|---|-------------|--------|
| 1 | Residual Effect/DE analysis + runtime rewrites → `EffectDispatch` / `DomainExpressionDispatch` | Product lower/print already use dispatch; analysis multi-site switches remain |
| 2 | Collapse dual DE rewrites in `DomainEntityInstance` (`BindPeerInExpression` / `PreprocessQuantifiers`) | Same composite shape reconstruction without dispatch base |
| 3 | `Runtime/` folder: `DomainEntityInstance`, `DomainInstanceStore`, `InvocationResult` | Execution vs model definition |
| 4 | Create-in IR collapse → `CreateEntityInstance.RelationshipName` at parse | Runtime already desugars; check C# export peer paths |
| 5 | Evolution mutation helpers (`ReplaceInList` / shared `ApplyTo` shapes) | Not a new evolution framework |
| 6 | Fix **Analysis → Lowering** reverse deps (type mapping / storage helpers) | Domain facts belong under Analysis long-term |
| 7 | Thread `PassRegistry` / pack inputs into product analyze/evolve | Placement issue, not type size |
| 8 | PolicyEvaluator: VM-primary; LINQ dual-oracle → tests only | CORE guidance |

**Intentional — keep:** PolicyEvaluator → Interpretation bridge; dual-path effects (VM-lowerable vs direct executor) as documented in domain execution model.

### 4.2 Placement tiers (decomposition proposal)

```text
Tier 1  Core types
Tier 2  Effects / Constraints / Bootstrap / Queries (+ Runtime/)
Tier 3  Builders
Tier 4  Evolution
Tier 5  Analysis
Tier 6  Lowering
optional later: Parsing → Poly/Dsl/  (parser still produces DomainChange/DE today)
```

---

## 5. Analysis metadata utilization (primary technical finding)

### 5.1 What “done” already means

DACR + DAS shipped a **product monopath** for name→member and several fail-closed consumers:

| Consumer | Bags |
|----------|------|
| Runtime resolve / notify | Catalog, relationship contracts, subscription dispatch plans, entity structure |
| Effective stage surface | `StageCapabilityMetadata` + `DomainEffectiveSurface` |
| Codegen | Storage / behavior / aggregate / entity structure |
| MCP | Partial projection via `get_domain_analysis` (structure, topology summary, behavior actions, storage/transport flags) |

**Contract (CORE):** semantic downstream paths require `AnalysisResult` and fail closed when required metadata is missing.

### 5.2 The gap: bags paid for, still rediscovered

Analysis is too often a **fan-out of independent tree walks**, not a **pipeline of facts**.

| Layer | Symptom |
|-------|---------|
| **Peer analysis passes** | `EffectAnalyzer`, `PolicyConstraintAnalyzer`, parts of Capability/RuntimeContract still `domain.Relationships.FirstOrDefault` / property scans instead of catalog helpers |
| **Empty `Dependencies`** | Semantic, Structural, EffectTopology, ConstraintPropagation, ContractIntegration — topology not a hard edge for most consumers |
| **Storage path** | `StorageAnalyzer` rebuilds unique/soft-delete/stage enum from properties even when `EntityStructureMetadata` exists |
| **Lowering residual** | Exporter + `EffectLoweringPass` still re-scan enums/types/relationships in places (phases 0–4 of analysis-consuming-lowering done; residuals remain) |
| **Runtime** | Selective: notify/catalog strong; large `DomainEntityInstance` paths still IR-first |
| **MCP** | Session holds rich `LatestAnalysis`; agents mostly see diagnostics + thin facts — re-infer hierarchy from DSL |
| **Domain → Interpretation** | Domain bags stop at domain boundary; AST program analysis does not inherit domain property/policy meaning |

### 5.3 Publisher inventory (high signal)

| Bag | Publisher | Typical consumers today |
|-----|-----------|-------------------------|
| `DomainCatalogMetadata` | DomainCatalogPass | Lookups, evolution, runtime, MCP describe |
| DTLM / RLM (intermediate) | SemanticDomainAnalyzer | Mid-pipeline; embedded in catalog |
| `EntityStructureMetadata` | EntityStructureAnalyzer | Runtime transition, export, MinimalApi |
| `StageCapabilityMetadata` | CapabilityAnalyzer | Effective surface, Behavior adapter |
| `SubscriptionDispatchPlanMetadata` | RuntimeContractAnalyzer | Store notify |
| `EffectTopology` / `OwnershipAggregate` | Topology + Aggregate | Storage/Transport; weak MCP |
| `BehaviorMetadata` | BehaviorPass | Codegen; partial MCP |
| `RequiredPropertiesMetadata` | RequiredPropertiesPass | EffectAnalyzer |
| `ResolvedRelationshipTargetMetadata` | EffectFactsPass | Effect lowering create-in |
| `StorageMapping` / `Transport` | Storage/Transport passes | Codegen; MCP flags only |
| Cross-reference / dependency graph | CrossReferencePass | Largely under-projected |

### 5.4 Opportunity suite (solidified)

**Queue:** [`simple-agent-tasks/amu-README.md`](simple-agent-tasks/amu-README.md) (W0–W4 + gate).  
**Status:** Ready — not CURRENT until master-roadmap admits.

**Anti-goal:** invent more metadata types without consumers. Prefer wire existing bags and delete dual scans.

---

## 6. Cross-cutting recommendation matrix

| If you optimize for… | Admit as CURRENT |
|----------------------|------------------|
| Trust bar / platform dogfood | **Dogfood wave 2** (see §7 companion note in chat; protocol already under `v2-to-v3/mcp-dogfood-*`) |
| Experiment language growth | **P1 temporal** or **P4 when any/all** — after short dogfood or explicit pick |
| Cohesive code structure | **Runtime/ + dispatch migration** |
| Analysis as single fact spine | **amu (metadata utilization)** W1–W4 |
| Stay idle | CURRENT = `(none)` |

**Do not** open dogfood + amu + temporal + decomposition in parallel.

---

## 7. Explicit non-goals (this findings pass)

- Re-executing archived suites  
- Multi-assembly split without external consumer  
- Intent-log product mutation path  
- Event/pub-sub surface (ADR: stage transition as observable)  
- Treating experiment Phase 2 checklist as one mega-suite  

---

## 8. Document maintenance

When a suite is admitted:

1. Update master-roadmap Agent pick `CURRENT:`  
2. Create `docs/plans/simple-agent-tasks/<suite>-README.md` (or revive dogfood under `v2-to-v3/simple-agent-tasks/`)  
3. Link this findings doc as orientation only  
4. On complete: archive micro-tasks; leave this doc as historical orientation or slim to pointers  

**Product guide and CORE** remain mechanism sources of truth; this file is planning orientation only.
