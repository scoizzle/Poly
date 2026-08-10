# DomainModeling — Delivered Capability Inventory

**Date:** 2026-07-24 (revised)  
**Purpose:** Single reference for what DomainModeling can actually do — by capability, with code references. Answers “are we building this or does it already ship?”  
**Not a task queue.** Agent pick: [`plans/v2-to-v3/master-roadmap.md`](plans/v2-to-v3/master-roadmap.md). Completed suites: [`plans/archive/domainmodeling-completed-2026-08/`](plans/archive/domainmodeling-completed-2026-08/README.md).

---

## 0. Architecture snapshot

```text
Domain (immutable)
  → DomainEvolution.Apply  → DomainModelAnalyzer (domain analysis pipeline)
  → DomainExpression lower → Syntax AST → DirectVmAbiEmitter → VM

Codegen (optional host):
  domain AnalysisResult
  → StoragePass (+ packs) [+ TransportPass]
  → DbContextGenerator / MinimalApiGenerator / HttpFileGenerator
```

| Concern | Owner |
|---------|--------|
| Domain facts + evolution gate | `Poly/DomainModeling` + `Syntax/Analysis` |
| VM execution | `Poly/Interpretation` |
| MCP session tools | `Poly.Mcp` (thin) |
| File codegen host | `src/Poly.DslCompiler` |

---

## 1. Domain authoring (definition surface)

> MCP column reflects the **minified catalog (2026-08-08)**: incremental structure via unified
> `add(kind, payload)` / `remove(kind, payload)`; bulk via `apply_dsl`. Per-type `add_entity` /
> `add_property` / … tools were deleted by the mcp-minify suite — do not re-add them without explicit admit.

| Capability | DSL | MCP Tool(s) | Core Code | Status |
|-----------|-----|-------------|-----------|--------|
| Entity definition | `Book: entity { }` | `add` kind=`entity` | `Entity.cs`, `Evolution/DomainChange.cs` | ✅ |
| Property with type | `Title: Text` | `add` kind=`property` | `Property.cs` | ✅ |
| Constraint: Required | `Name: Text required` | `add` kind=`constraint` (type=Required) | `Constraints/RequiredConstraint.cs` | ✅ |
| Constraint: Unique | `ISBN: Text unique` | `add` kind=`constraint` (type=Unique) | `Constraints/UniqueConstraint.cs` | ✅ |
| Constraint: Range | `Age: Number range(0, 150)` | `add` kind=`constraint` (type=Range, min/max) | `Constraints/RangeConstraint.cs` | ✅ |
| Constraint: Length | `Code: Text length(1, 50)` | `add` kind=`constraint` (type=Length, min/max) | `Constraints/LengthConstraint.cs` | ✅ |
| Constraint: Pattern | `Email: Text pattern("…")` | `add` kind=`constraint` (type=Pattern, pattern) | `Constraints/PatternConstraint.cs` | ✅ |
| Constraint: Default | `Status: Text default(Active)` | — | `Constraints/DefaultValueConstraint.cs` | ✅ |
| Constraint: Enum | `Status: PatronStatus` | — | `Constraints/EnumConstraint.cs` | ✅ |
| Enum type | `Genre: enum { Fiction, … }` | — | `EnumType.cs` | ✅ |
| Stage | `Active: stage { }` | `add` kind=`stage` | `Stage.cs` | ✅ |
| Action | `Submit: action { }` | `add` kind=`action` / `stage_action` | `Action.cs` | ✅ |
| Action parameters | `CheckOut: action (book: Book) -> Loan` | — | `Action.cs` | ✅ |
| Require gates | `require GoodStanding` | `add` kind=`policy` (entity) / `apply_dsl` | `Policy.cs` | ✅ |
| Stage entry/exit | `entry { }` / `exit { }` | — | `Stage.cs` | ✅ |
| Stage subscription | `when loans Overdue { }` | — | `StageSubscription.cs` | ✅ |
| DSL apply / export | — | `apply_dsl`, `export_dsl` | `Parsing/PolyDslParser.cs`, `DomainDslPrinter.cs` | ✅ |

**Key files:** `Evolution/DomainEvolution.cs` (analysis-gated Apply), `Bootstrap/DomainFactory.cs`, `src/Poly.DslCompiler/DslCompiler.cs` (file host).

---

## 2. Relationship modeling

| Capability | DSL / surface | Code | Status |
|-----------|---------------|------|--------|
| OneToMany nav | `loans: many Loan` | `Relationship.cs`, nav on entities | ✅ |
| OneToOne / to-one | `book: Book` | same | ✅ |
| Back-reference | `borrower: Patron` | same | ✅ |
| Source-ownership | `SourceOwnsTarget` | `Relationship.cs` | ✅ |
| `create in Rel` auto-wire | `create in loans { }` | `Effects/CreateEntityInRelationshipEffect.cs` | ✅ |
| MCP link existing | `link_instances` | `Poly.Mcp/Tools/RuntimeTool.cs` | ✅ (`7d067c0`) |
| Link/unlink IR | no DSL keyword | `LinkRelationshipEffect` / `UnlinkRelationshipEffect` | ✅ MCP link; unlink library-only |

---

## 3. Expression system

### 3.1–3.4 Base, arithmetic, boolean, comparisons

All of: property/param/literal access; `+ - * /`; `and`/`or`/`not`; `== != < <= > >=` and `is` — **shipped** in DSL + DE + lowering (`DomainExpression.cs`, `Lowering/DomainExpressionLoweringPass.cs`).

### 3.5 Relationship / navigation reads (Q1′)

| Expression | DSL | DE | Status |
|-----------|-----|-----|--------|
| Path-prefix to-one | `customer Tier` | `RelationshipNavigation` | ✅ |
| Exists / absence | `assignee exists` / `not assignee exists` | `Exists` / `NotExists` | ✅ |
| To-one `where` | `customer where Status is "Active"` | desugared AND-chain | ✅ |
| Owned access | `profile Field` | `OwnedAccess` | ✅ IR + limited DSL; deepen if dogfood needs |

### 3.6 Collection quantifiers (Q3′)

| Form | Empty set | Status |
|------|-----------|--------|
| `any Rel where …` | false | ✅ |
| `all Rel where …` | **false** (no vacuous true) | ✅ |
| `none Rel where …` | true | ✅ |
| `count Rel` / filtered | 0 | ✅ |

Runtime: store-linked `DomainEntityInstance` preprocess + VM. Analysis: OneToMany-only (fail-closed shapes; codes in `DomainModelDiagnosticCodes` / effect analyzers, e.g. **DMEFF007** invoke/quantifier shape).

### 3.7 Date operations

| Capability | Status |
|-----------|--------|
| `DateOperation` IR (AddDays/AddMonths/DiffDays) + lowering | ✅ IR |
| Product DSL for date ops | ⬜ Pull |
| `now` / `today` literals | ✅ shipped |

---

## 4. Effect system

| Effect | DSL | Status |
|--------|-----|--------|
| `transition to Stage` | yes | ✅ |
| `assign Prop to expr` | yes (local target only) | ✅ |
| `create Type { }` | yes | ✅ |
| `create in Rel { }` | yes | ✅ |
| `delete` (soft-delete self) | yes | ✅ E1 |
| `invoke Action` / `invoke Rel.Action` | yes | ✅ E3a/E3b |
| `invoke any\|all Rel.Action [where …]` | yes | ✅ E3b + DMEFF007 |
| `if (expr) { } else { }` | yes | ✅ E4 |
| Link / unlink | **no DSL**; MCP `link_instances`; library unlink | ✅ / 🟡 |

Cross-entity **assign writes** banned (query §3.1). Peer mutation via create-in / link / invoke only.

---

## 5. Analysis pipelines (accurate home)

### 5.1 Domain pipeline — always on evolve / MCP analysis

Registered in `UseDomainModelAnalysisPipeline()` (`DomainModelAnalyzer.cs`):

| Area | Passes (representative) | Status |
|------|-------------------------|--------|
| Structure / semantics | `StructuralDomainAnalyzer`, `SemanticDomainAnalyzer` | ✅ |
| Policy / effects | `PolicyConstraintAnalyzer`, `EffectAnalyzer`, `EffectOrderingAnalyzer` | ✅ |
| Constraints / enums | quality, propagation, enum subset | ✅ |
| Capabilities / rules / contracts | `CapabilityAnalyzer`, `RuleCoverageAnalyzer`, `ContractIntegrationAnalyzer` | ✅ |
| Entity structure | `EntityStructureAnalyzer` (key, root, stages) | ✅ |
| Subscriptions | contract, causality, replay safety | ✅ |
| Effect topology | `EffectTopologyPass` — cross-entity coupling | ✅ domain pipeline · **DAU:** algorithm still under Lowering (wrapper) |
| Ownership/aggregate | `OwnershipAggregatePass` — root/child hierarchy | ✅ domain pipeline · **DAU:** same mid-migration pattern |
| Behavior metadata | `BehaviorPass` — actions, parameters, transitions | ✅ domain pipeline · **DAU:** same |
| Authoring suggestions + C# entity IR | `AuthoringSuggestionAnalyzer`, `EntitySyntaxPass` | ✅ |
| Cycle detection | `CrossReferencePass` — entity dependency graph | ✅ domain pipeline · pack/coupling surface — **do not delete as unused** |

**Migration complete:** archived DAU (`dau-*`). Storage + Transport are always-on domain pipeline passes. Thin `*Pass` → `Lowering/*Analyzer` bridges eliminated.

### 5.2 Codegen pipeline — `DslCompiler.GenerateAllFiles` (today)

Topology/aggregate/behavior/storage/transport metadata all come from domain analysis (`priorAnalysis`). Codegen is emit-first — no second fact world for domain metadata. Pack storage conventions refine via narrow `StoragePass` fallback in `DslCompiler` (only when conventions differ from domain pipeline defaults). **Do not** treat Transport as dead code; packs will consume operational surfaces.

| Pass | Output | Domain fact? | Status |
|------|--------|--------------|--------|
| `StoragePass` | `StorageMappingMetadata` | **Yes (shape)** + pack maps | ✅ always-on domain pipeline; pack refinement via `DomainAuthoringContext` |
| `TransportPass` | `TransportMetadata` | **Yes (exposable surface)** | ✅ always-on domain pipeline; keep for packs |
| Pack `PassRegistry` | storage enrichment | Pack-specific | ✅ stays pack-attached; prefer prior domain result |

Fail-closed: missing storage (db/all); missing behavior/aggregate (all) → `InvalidOperationException` in `DslCompiler`.

### 5.3 Framework

| Piece | Location |
|-------|----------|
| Analyzer builder / context / result | `Poly/Syntax/Analysis/` |
| Node replacement | `NodeReplacementMetadata` |
| Interpretation semantic passes | `Poly/Interpretation/Analysis/` |
| Policy VM path | `DomainEntityInstance.EvaluatePolicy` → `DomainExpressionLoweringPass` → `Interpreter` |

---

## 6. Runtime execution

### 6.1 Instance + store

| Capability | Status |
|-----------|--------|
| Create / snapshot / stage / soft-delete | ✅ |
| `InvokeAction` with params, depth limit, policies | ✅ |
| `EvaluatePolicy` local vs store-linked (quantifiers) | ✅ |
| `DomainInstanceStore` Add/Remove/Link/Unlink/GetRelated/NotifyTransition | ✅ |

### 6.2 Policy evaluation modes

| Mode | Path | Status |
|------|------|--------|
| Local bag | `evaluate_policy(age|properties=)` | ✅ |
| Store-attached | `create_instance` → `link_instances` → `evaluate_policy(…, instanceId=)` | ✅ |
| Library | `DomainEntityInstance` + store | ✅ |

### 6.3 VM

`DirectVmAbiEmitter` + `VmState` canonical; LINQ generator secondary/oracle only.

---

## 7. Code generation

| Output | Mode | Path | Status |
|--------|------|------|--------|
| Entity types | all | IR (`EntitySyntaxMetadata` → `CSharpGenerator`) | ✅ |
| EF DbContext | db, all | IR (`DbContextGenerator.GenerateCompilationUnit`) | ✅ `c5d2220`/`b394a0e` |
| Minimal API Program | all | IR (`MinimalApiGenerator.GenerateCompilationUnit`) | ✅ |
| demo.http | all | **string** (`HttpFileGenerator`) | ✅ intentional |
| File name | — | `{domain.Name}DbContext.cs` | ✅ |

`Generate()` on DbContext/MinimalApi is a thin IR wrapper (no dual StringBuilder body).

Infra suite (Groups 1–7 under bar): **complete** — [`plans/infrastructure-pass-NEXT.md`](plans/infrastructure-pass-NEXT.md) · archive [`plans/archive/infrastructure-pass/`](plans/archive/infrastructure-pass/README.md).

---

## 8. MCP tool surface (summary)

| Area | Tools (representative) |
|------|------------------------|
| Session / domain | `create_domain_session`, `list_sessions`, overview/detail/analysis/suggestions, `get_relationships`, `get_constraints` |
| Unified evolve | `add` / `remove` (kind + payload — 8 kinds; policy remove supports stageName/actionName scope; constraint remove not implemented) |
| DSL | `apply_dsl`, `export_dsl`, `get_dsl_guide` |
| Policy | `add` kind=`policy`, `get_policy_expression`, `evaluate_policy` (+ `instanceId`) |
| Runtime | `create_instance`, `link_instances`, `unlink_instances`, `get_instance`, `list_instances`, `invoke_action` |
| Oracle | `simulate_policy` (DSL fragment), `describe_domain_element`, `export_domain_to_csharp` |

Full names: `Poly.Mcp/Tools/DomainTools.cs`, `RuntimeTool.cs`, `OracleTool.cs`.

---

## 9. Product DSL constructs (Phase 1a/1b)

Authoritative syntax: **`Poly.Mcp/Docs/poly-dsl-guide.md`** (keep in sync with parser).

Shipped highlights: entities, properties, constraints, enums, navs, stages, actions, params, require, policies, entry/exit, subscriptions, transition/assign/create/create-in/delete/invoke/if, quantifiers + path-prefix + exists + where, `column`/`table` annotations, `owned`.

---

## 10. Not yet shipped / pull

| Capability | Notes | Status |
|-----------|--------|--------|
| **Analysis pipeline merge (APM)** | Registration of topo/agg/beh/crossref on domain pipeline | ✅ **Done** |
| **Domain analysis unification (DAU)** | Storage/transport always-on, authoring context, emit-first | ✅ archived [`dau-*`](plans/archive/domainmodeling-completed-2026-08/simple-agent-tasks/dau-README.md) |
| CrossReference / coupling surface | Cycle + graph facets | ✅ registered · pack-ready — don’t delete |
| Transport in domain analysis | Exposable surface + packs | ⬜ DAU D3 (not “delete unused”) |
| Bar B full string oracle | Anonymous-object Syntax needed | ⬜ Pull |
| RestApi / MinimalApi / `.http` | **Transport implementation** (codegen) — consumes domain **Transport** + ownership/contracts/behavior; not an Analysis bag | ⬜ Pull (emit path) |
| StorageAccessPass | No consumer | ⬜ Pull |
| Q4 aggregates sum/min/max | No demand | ⬜ Pull |
| Date ops DSL | IR only | ⬜ Pull |
| Deeper owned expressions | Limited today | 🟡 |
| `unlink_instances` MCP | Library only | ⬜ Pull |
| Link **DSL** keyword | MCP + create-in suffice | ⬜ Pull |
| E5 micro effect tools | Dogfood-only | ⬜ Pull |
| JSON policy = DSL quantifiers | Documented weaker split | ⬜ Pull |

**Removed stale rows:** uncommitted qe/ip/link batches — **committed** (`85d28fe`, `7d067c0`, `c5d2220`, `b394a0e`).

---

## 11. Architectural boundaries

| Boundary | Enforcement |
|----------|-------------|
| DomainModeling ↛ Interpretation (except intentional bridges) | `DomainEntityInstance.EvaluatePolicy` → `DomainExpressionLoweringPass` → `Interpreter` |
| Interpretation → Syntax, Introspection | One-way |
| MCP thin | No domain mutation semantics in tools |
| Immutable domain | `DomainEvolution.Apply` + analysis gate |
| Analysis metadata | `IAnalysisMetadata`, not side tables |
| Node replacement | Analysis rewrite; backends honor replacements |
| No domain VM opcodes | Generic AST only (`CORE.md`) |

---

## 12. Suggested reading order

1. This inventory (what ships)  
2. [`plans/v2-to-v3/master-roadmap.md`](plans/v2-to-v3/master-roadmap.md) (product pick / CURRENT)  
3. [`plans/domainmodeling-workstream-map.md`](plans/domainmodeling-workstream-map.md) (done vs parked)  
4. [`plans/infrastructure-pass-NEXT.md`](plans/infrastructure-pass-NEXT.md) (codegen pull)  
5. [`CORE.md`](CORE.md) before changing pipeline seams  
6. Archived suite history: [`plans/archive/domainmodeling-completed-2026-08/`](plans/archive/domainmodeling-completed-2026-08/README.md)
