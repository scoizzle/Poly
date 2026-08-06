# Domain analysis — future state

**Date:** 2026-07-30  
**Status:** Target architecture (plan)  
**Audience:** Agents and humans shaping DomainModeling analysis, runtime, MCP, and export  
**Companion (current inventory / migration steps):** [`domain-analysis-simplification.md`](./domain-analysis-simplification.md)  
**Agent task queue (execute here):** [`simple-agent-tasks/das-README.md`](./simple-agent-tasks/das-README.md)  
**Foundations:** [`docs/CORE.md`](../CORE.md) · [`domain-analysis-unification.md`](./domain-analysis-unification.md) · DACR fail-closed contract  

This document describes **where domain analysis is going**, not a catalog of today’s bugs. Use the companion inventory for pass-by-pass present state and tactical cutovers. **Kick off work via the `das-*` suite**, not by freelancing from this plan alone.

---

## 1. North star

Domain analysis is the **single semantic memory** of an immutable `Domain`.

After `DomainModelAnalyzer.Analyze(domain)`:

- Every consumer that needs meaning (runtime, MCP, evolution, lowering, export, packs) reads **`AnalysisResult`**.
- No consumer re-derives static contracts by scanning the domain tree for “the real answer.”
- No consumer treats missing required metadata as empty success.
- Analysis publishes **facts and diagnostics**. It does **not** emit host Syntax IR, C#, SQL text, or HTTP routes.

```text
                    Authoring / evolution / MCP apply
                                    │
                                    ▼
                           immutable Domain
                                    │
                    ┌───────────────┴───────────────┐
                    │   Domain analysis pipeline     │
                    │   validate · catalog · derive  │
                    └───────────────┬───────────────┘
                                    ▼
                            AnalysisResult
                     facts + diagnostics + nothing else
                                    │
          ┌─────────────┬───────────┼───────────┬─────────────┐
          ▼             ▼           ▼           ▼             ▼
      Runtime       MCP/oracle   Evolution   Lowering      Export/packs
   dispatch maps    describe     mutate by   effects→AST   Syntax / SQL /
   instances        queries      catalog     DE→AST        transport emit
```

**Product consequence:** generation, simulation, and tooling stay honest because they share one semantic surface. Dogfood pain hits the catalog and the pipeline, not a private re-walk in each tool.

---

## 2. Principles (future invariants)

These are non-negotiable for the target design. New work should not violate them even before full migration.

| # | Principle | Implication |
|---|---|---|
| P1 | **One catalog** | Name → type / entity / relationship / stage / action / policy is answered by **one** domain-scoped metadata product (and small pure helpers over it). No parallel “MTI vs ARM vs DTLM” ownership of the same graph. |
| P2 | **Analysis = facts + diagnostics** | Projection, pretty-print, and host codegen are **downstream of** `AnalysisResult`, never pipeline passes. |
| P3 | **Fail closed on required facts** | If a path needs a bag and analysis ran, missing bag → loud error. “Not found” ≠ “metadata absent.” |
| P4 | **Lowering depends on Analysis** | Effect lowering, export, and runtime consume metadata. Analysis must not call into export/codegen. |
| P5 | **Declared dependencies** | Every pass either declares accurate `Dependencies` or is explicitly pure-lint with no metadata writes that others read. |
| P6 | **One algorithm per semantic question** | “Effective policies at stage,” “resolve action with SA fallthrough,” “is this entity a root?” — one implementation, many callers. |
| P7 | **Packs refine, they don’t re-analyze the domain** | Storage/transport packs overlay type maps and conventions on core facts; they do not invent a second domain world. |
| P8 | **Dual paths are temporary** | Tree scans tagged for removal are migration debt, not architecture. Target: zero semantic dual paths when analysis is present. |
| P9 | **Stable keys** | Metadata is keyed consistently (domain / entity / stage / `default` as documented). Consumers never guess the key. |
| P10 | **Tests prove contracts** | Fail-closed and lookup tests withhold metadata or force each dual path during migration; happy-path presence checks are not enough. |

---

## 3. Future pipeline shape

Three **stages**, not twenty ad-hoc registrations. Implementation may still be multiple `INodeAnalyzer` types, but they map cleanly into these stages.

### 3.1 Stage V — Validate

**Job:** Is this domain well-formed enough to reason about?

| Concern | Future home | Output |
|---|---|---|
| Tree shape (duplicate names, ownership cardinality) | Structural validate | Diagnostics only |
| Reference integrity (types, relationship ends, create targets) | Semantic validate | Diagnostics (+ optional resolved-ref attachments on nodes) |
| Policy / constraint / effect rules | Rule packs (core + optional severity tiers) | Diagnostics only |
| Subscription contract / causality | Subscription validate | Diagnostics only |
| Authoring suggestions | Optional advisory pack | Info/hint diagnostics |

**Does not:** build large indexes, storage models, or Syntax IR.

Structural failure still short-circuits derivation (today’s `HasStructuralFailure` idea preserved).

### 3.2 Stage C — Catalog

**Job:** Build the **single** navigable map of the domain.

**Primary product (target name):** `DomainCatalogMetadata` (working title).

Logical contents (one record or a tightly versioned family):

| Slice | Answers |
|---|---|
| Types / entities | Resolve type name; enumerate entities |
| Relationships | Resolve relationship name; ends; ownership flag; cardinality |
| Stages | Stages by entity; stage by name |
| Actions | Entity-level and stage-scoped actions by name |
| Policies | Entity / stage / action policy maps |
| Members (optional slice) | Effective properties/actions/stages if still needed as a denormalized view |

**Rules:**

- Published once per successful analyze, keyed on the **domain** node (or a documented single key).
- All lookup APIs (`TryGetEntity`, `TryResolveAction`, evolution target resolve, MCP describe) are **thin functions over the catalog**.
- Runtime-specific *plans* (e.g. subscription dispatch tables keyed by stage **node**) may still exist as Stage D products **built from** the catalog, not as a second copy of the action/policy graph.

### 3.3 Stage D — Derive

**Job:** Expensive or pack-facing views that are not the catalog itself.

| Product | Purpose | Built from |
|---|---|---|
| Entity structure | Key, soft-delete, root?, ctor order, stage map handle | Catalog + constraints |
| Effect topology | Cross-entity create-in / invoke / subscription edges | Catalog + effect walk once |
| Ownership aggregate | Roots and children | Topology + structure |
| Capability surface | Effective actions/policies per stage; transition targets | Catalog + hierarchy rules (**one** effective-* algorithm) |
| Behavior surface (optional) | Pack-shaped action DTOs for codegen | Capability or catalog |
| Storage mapping | Columns, FKs, tables | Aggregate + topology + pack type maps |
| Transport surface | Exposable API nesting | Aggregate + topology + pack protocol |
| Cross-entity dependency graph | Cycles / coupling diagnostics | Topology |

**Order:** Catalog → structure → topology → ownership → capability → storage/transport (and peer derives). Explicit deps, not registration luck.

**Not in Stage D:** host Syntax trees, C# source, SQL scripts, `.http` files.

---

## 4. Future metadata contract (consumer-facing)

### 4.1 Required vs optional

| Consumer class | Required when analysis is present | Optional / pack |
|---|---|---|
| Runtime instance dispatch | Catalog; entity structure; relationship contracts; subscription plans | — |
| MCP semantic describe / effect tools | Catalog; capability (or equivalent effective view) | Authoring hints |
| Evolution mutations | Catalog | — |
| Effect lowering | Catalog; create-in resolution facts | — |
| Entity / program export | Full `AnalysisResult` (catalog + structure at minimum) | Storage, transport, behavior |
| DB / API pack emit | Storage / transport bags | Dialect overlays |

Missing **required** → throw or tool error with an explicit code. Missing **optional** → feature-specific degrade only if product-defined (default: fail closed for semantic routes).

### 4.2 Lookup API (stable surface)

A single public extension surface (evolution of today’s `DomainSemanticLookupExtensions`):

```text
TryGetEntity(name)
TryGetRelationship(name)
TryGetStage(entity, stageName)
TryResolveAction(entity, currentStage, actionName)   // SA fallthrough is here only
GetEffectivePolicies(entity, stageName)              // one composition rule
GetEffectiveActions(entity, stageName)               // same family
```

Implementation: catalog + pure functions. No second copy of SA in runtime “scan” code in the end state.

### 4.3 Keying convention (documented once)

| Metadata kind | Key |
|---|---|
| Domain-wide catalog / topology / ownership / storage / transport / behavior | Domain node |
| Entity structure / action resolution views if not inlined | Entity node |
| Stage capability / subscription plan | Stage node |
| Resolved type on a type-reference node | That type-reference node |
| Global singleton lookups (only if needed during migration) | `default` — **migrate away** for new bags |

---

## 5. Future consumer model

### 5.1 Runtime

```text
DomainEntityInstance / DomainInstanceStore
  → RuntimeAnalysisCache.GetOrAnalyze(Domain)
  → catalog + structure + dispatch plans
  → invoke / transition / notify
```

- Standalone instances (`Domain == null`) are an **explicit** mode: either unsupported for semantic dispatch, or supported only with a documented reduced contract (no subscriptions, no catalog). Not a silent second implementation of SA forever.
- No residual `DM-META-REMOVE-FALLBACK` scans in the end state when `Domain` is bound.

### 5.2 MCP / oracle

- Session always holds `LatestAnalysis` after successful analyze.
- Semantic tools require it (already directionally true for effects).
- Describe routes: catalog + capability only; no tree rediscovery when analysis is present.
- “Not found” is a successful lookup against a complete catalog.

### 5.3 Evolution

- Mutation context always receives catalog (or fails to build).
- In-batch live tree resolution is either folded into catalog invalidation/reanalyze or a **single** documented live-overlay mechanism—not ad-hoc scans in every finder.

### 5.4 Lowering (effects / DE)

- `EffectLoweringPass` requires analysis for semantic effects when domain-backed.
- Create-in / type resolve / relationship resolve use catalog (or derived create-in facts), never “scan Domain.Relationships if null analysis” in product paths.

### 5.5 Export and packs

```text
AnalysisResult
  → DomainProgramProjection.ToSyntax(domain, analysis)   // export layer
  → CSharpGenerator / SQL / MinimalApi / RestApi emit
```

- **No** `EntitySyntaxPass` in the analysis pipeline.
- Projection takes `AnalysisResult` (or a metadata provider that is not “AnalysisContext cast to AnalysisResult”).
- Packs may re-run **Storage/Transport refinement** with type maps and conventions, consuming prior topology/ownership from the core result—not re-deriving the whole domain.

---

## 6. Future pass layout (conceptual)

Fewer concepts; more honest names. Exact type names can evolve.

```text
Validate
  StructuralValidate
  SemanticValidate          // reference integrity; may attach ResolvedTypeReference
  PolicyValidate            // diagnostics (+ RequiredProperties if still a fact)
  EffectValidate            // diagnostics (+ create-in resolve fact if kept)
  SubscriptionValidate
  (optional) Quality / Coverage / Suggestion packs

Catalog
  DomainCatalogPass         // ONE index publisher

Derive
  EntityStructurePass
  EffectTopologyPass
  OwnershipAggregatePass
  CapabilityPass            // single effective-* surface
  CrossReferencePass        // optional / diagnostic-heavy
  StoragePass               // pack inputs via ctor / analysis inputs
  TransportPass
  (optional) BehaviorAdaptPass  // only if packs need a distinct DTO shape
```

**Gone from the pipeline:**

- EntitySyntax / any host IR pass  
- Parallel “runtime contract” pass that re-indexes the same maps as the catalog (subscription **plans** may remain as derive)  
- Unnamed empty-`Dependencies` fact publishers  

---

## 7. End-to-end product loops (future)

### 7.1 Author → analyze → simulate

```text
DSL / MCP / fluent evolution
  → Domain
  → Analyze → AnalysisResult
  → DomainEntityInstance(Domain, …) uses catalog
  → transitions and subscriptions match analysis truth
```

### 7.2 Author → analyze → generate

```text
Domain + AnalysisResult
  → projection / storage / transport emit
  → host project (C#, SQL, API)
  → no second semantic discovery in generators
```

### 7.3 Author → analyze → tool honesty

```text
MCP describe / lower_effect / get_domain_analysis
  → same AnalysisResult as runtime and export
  → capabilities claimed only if metadata exists
```

These three loops are **one platform**, not three stacks.

---

## 8. Quality bar for the future pipeline

| Bar | Definition |
|---|---|
| **Coherence** | One answer per semantic question; documented keys |
| **Completeness** | Catalog covers every entity/relationship/stage/action/policy in the tree |
| **Fail-closed** | Required bags missing → error; tests strip bags and assert throw |
| **Sibling-path free** | No “metadata branch correct, scan branch wrong” in the end state |
| **Dependency honesty** | Pass deps match real reads |
| **Export purity** | AnalysisResult builds without calling generators |
| **Incremental-ready** | Catalog and derives invalidate by domain/entity scope without full dual implementations |
| **Agent-legible** | CORE + this plan name the required bags; no ghost metadata |

---

## 9. Migration strategy (toward the future, not a dump of today)

Work is ordered by **value to the future shape**, not by historical pass number.

| Wave | Outcome | Aligns with |
|---|---|---|
| **W0** | Projection lives only in export; analysis never produces Syntax IR | S0 in simplification plan |
| **W1** | Single catalog; all lookups re-homed; duplicate index publishers deleted or dual-write then delete | S1 |
| **W2** | Single effective-action/policy algorithm; Capability is the view; Behavior is adapter or gone | S2 |
| **W3** | Validation packs separated from fact emitters; deps accurate; Effect/Policy megapasses thinned | S3 |
| **W4** | No semantic tree scans when analysis present; DACR Done Definition met | S4 / DACR suite |

**Rules of migration:**

1. Prefer **delete dual path** over documenting it.  
2. Prefer **rehome consumer to catalog** over adding a new bag.  
3. Prefer **export-time projection** over mid-pipeline IR.  
4. Every wave leaves CORE and this plan accurate.  
5. Follow-ups and reviews stay in `docs/plans/` (agent rule).

Detail of present-day pass inventory and EntitySyntax failure modes: **[`domain-analysis-simplification.md`](./domain-analysis-simplification.md)**.

---

## 10. Explicit non-goals (still true in the future)

- Domain-specific VM opcodes  
- Second product IR beside Syntax AST for execution  
- RestApi route/DTO layout as analysis metadata (emit consumes transport facts)  
- Dialect-specific SQL as core always-on defaults  
- Entity inheritance revival  
- Framework completeness of every diagnostic pack before catalog ships  
- Making standalone (`Domain == null`) instances full peers of domain-bound runtime  

---

## 11. Success picture (how we’ll know we’ve arrived)

Checklist status after **DAS W4.3 close** (2026-07-31). Suite gate: [`simple-agent-tasks/das-gate.md`](./simple-agent-tasks/das-gate.md) (Wave 4 + suite **complete**).

1. [x] **Agent can explain** the pipeline in three stages (Validate / Catalog / Derive) without listing twenty pass names. *(CORE §3.1 + this plan; pass inventory is implementation detail)*  
2. [x] **Grep** for `DM-META-REMOVE-FALLBACK` in DomainModeling semantic routes is empty. *(W4.3: `rg` over `**/*.cs` = 0; EffectLowering/MinimalApi/export ctor monopaths fail-closed under analysis)*  
3. [x] **DslCompiler** entity emit never reads `EntitySyntaxMetadata` from analysis. *(W0: EntitySyntaxPass/Metadata deleted; emit via `DomainProgramProjection.ToSyntax`)*  
4. [x] **One type** is the authoritative catalog; MTI/ARM/DTLM-as-separate-owners are gone or are pure type aliases. *(W1.4: catalog sole publisher; ARM/MTI not published)*  
5. [x] **Runtime, MCP, export** all call the same lookup helpers for action/policy/relationship resolution. *(W1.3 + W4.1–W4.3: `DomainSemanticLookupExtensions` / catalog / ESM ctor order)*  
6. [x] **EntitySyntax-class failures** cannot soft-skip entity generation: export fails loud or succeeds fully. *(W0.2: projection missing defs throw)*  
7. [x] **New feature** that needs “find action by name” does not add a pass—it extends the catalog or a pure helper. *(W1 catalog design; no new parallel indexes)*

---

## 12. Decision log (future-facing)

| Decision | Choice |
|---|---|
| Where does Syntax IR for entities live? | Export / projection only |
| How many name-resolution indexes? | One catalog |
| Who owns SA fallthrough? | Single helper on the catalog API |
| Are diagnostics part of analysis? | Yes, but not a substitute for missing facts |
| Do packs re-analyze domains? | No; they refine storage/transport inputs |
| Is analysis optional for semantic product paths? | No |

---

## 13. Next concrete step

**DAS W0–W4 landed (2026-07-31).** Markers zero; analysis-present soft dual paths removed on scoped semantic routes including `EffectLoweringPass.GetConstructorParameterOrder` (ESM required; no property-order rebuild). Analysis-null structural rebuild retained only as the standalone (`Domain == null`) reduced contract — non-goal to make it a full peer. Suite Done Definition met — see [`simple-agent-tasks/das-gate.md`](./simple-agent-tasks/das-gate.md).

Ongoing: do not reintroduce semantic dual paths or mid-pipeline Syntax IR; prefer catalog/helpers for new name resolution; keep CORE and this plan accurate when mechanisms change.

Treat this document as the **acceptance target** for design reviews of any new analysis metadata or pass.
