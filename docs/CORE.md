# Poly Core Reference

**Job:** Purpose, boundaries, and **machinery you must not reinvent**.  
**Not this doc:** Plans, ADR history, product recipes, pass-writing tutorials.

**Load:** [`AGENTS.md`](../AGENTS.md) **Core tenets** first. Then this file before changing `Poly.Ast`, `Poly.Analysis`, `Interpretation`, `Introspection`, `DomainModeling`, or `Poly.Mcp`. Keep it short — link out instead of growing this file.

**Maintenance:** Update this file in the same change that alters a listed mechanism. Stale CORE is worse than no CORE.

**Frozen vs current:** §0 = architecture (must respect). §3 = **current** machinery — compose it; do not freeze consumer shapes or grow dual-paths. Policy: [`docs/decisions/2026-09-04-frozen-core-pipeline.md`](decisions/2026-09-04-frozen-core-pipeline.md).

---

## 0. Frozen core

```text
Domain (facts + uses ids)
  → DomainSession (libraries = compilation units: analyzers + maps + artifact contributors)
  → session.Analyze  → bags on nodes, replacements
  → dirty → STOP
  → session.Lower → artifact set on the session (fail closed if empty when required)
       · Syntax operation module (privileged: meaning + Interpreter sim; no bags)
       · other library producer artifacts (host / HTTP / … — delivery; call/bind the module)
  → consumers bind the module; they do not fork analyze/lower
MCP: harness — not a product door
```

| Frozen | Implication |
|--------|-------------|
| Nodes are the symbolic primary | No product-path primitive IR; shipped meaning = complete Syntax tree |
| Analysis owns facts and rewrite | Bags on nodes; `SetNodeReplacement`; semantic consume needs `AnalysisResult` |
| Libraries extend the session | `uses` ids; compilation unit = library; unknown/duplicate ids fail closed |
| Post-analyze Lower → artifact set | Catalog on session; Syntax module privileged for meaning + sim; other artifacts ≠ second sim |
| Doors map the catalog | Opt-in hosts; no core `Main`; doors do not invent operations |
| Do not add consumer-specific lowering flags | No `UseThisReference` twins; no Lower-inside-analysis |

**Forbidden:** Effect/Domain walk as shipped meaning; emitter/ABI one-offs; MCP as customer API; treating scratch store / `Stay.Create` / Store job names as architecture; DEI/MCP walk as product proof (the module is).

Always-on summary: [`AGENTS.md`](../AGENTS.md) Frozen core · Agent target.

---

## 1. Purpose

Neurosymbolic platform: author structured domain → lower to symbolic AST → analyze → VM (canonical). Domain = library of legal operations (no required `Main`). Goal: shipped correct end-to-end behavior, not framework completeness.

**Current consumers:** lower → Interpreter (module dictionary, host `ModuleInstanceExecutor`) | `session.Emit` (C# module) + bag-gated host files. MCP: `create_instance` → `evaluate_policy` / `invoke_action`.

**Emit gate:** `session.Emit` registers the `vm-analysis-report`, then refuses (throws the first VM error) when the interpretation analysis of the lowered module reports an error; the report and the catalog stay on the session.

**Words (current bind, not frozen):** `Domain` = facts. Analysis publishes **bags**. Surfaces (`uses sqlite` / `uses http`) select implementations. Lowering **reads** bags; the operation tree has no bag types. Bind is caller-supplied. Detail: [`docs/decisions/2026-09-03-facts-concerns-bags-store-bind.md`](decisions/2026-09-03-facts-concerns-bags-store-bind.md).

| Hard line | Implication |
|-----------|-------------|
| Domain is a module, not a process | No `Main` / `Program.cs` in core |
| Shipped ⊆ lowerable | Gaps stay in `docs/plans/`, not the parser/guide |
| Always-legal operations | Full tree per op; no `Comment` / `null` / host walk as meaning |
| AST is symbolic primary | No parallel primitive IR on product paths |
| VM executes Syntax programs | `Interpreter.Compile` fail-closed; no domain opcodes; C# emit is a projection |
| Domain lowers to generic ops | StageTransition / invoke / create / unique / clocks → existing Syntax + Store jobs on the one module |
| Product doors are opt-in | `uses`; CLI seeds ids only |
| MCP is harness | Author / inspect / simulate with caller context — not customer API |
| Lowered module is **domain meaning** | Artifact set on session; Syntax module privileged; fix Lower when sim ≠ print |
| Extend in the pipeline | Lower / analyze / replace — not emitter/ABI patches |
| Analysis required for semantics | Fail closed without `AnalysisResult` |
| One coherent path | Compose existing mechanisms |
| Immutable domain boundary | `DomainEvolution`…`Apply` only |

TFM: `net10.0`, nullable on, zero external deps in core `Poly/`. Policy: [`docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md`](decisions/2026-08-15-domain-library-extensions-mcp-harness.md).

---

## 2. Separation of concerns

| Concern | Owns | Must not |
|---------|------|----------|
| **Ast** | `Node`, `NodeId`, fluent API | Execution, domain shapes, analysis |
| **Analysis** | Framework, metadata, **node replacement** | Execution, domain concepts, MCP |
| **Interpretation** | Semantic passes, `Interpreter`, `AbiEmitter`, VM | Domain concepts; ABI forks for one consumer |
| **Introspection** | Host-neutral type/member model (CLR = first provider) | Depending on Interpretation |
| **DomainModeling** | Immutable `Domain`, evolution, DE→AST per op; contracts via `bind` | Domain opcodes; `Main`; growing `Comment` as meaning |
| **Grammar** | Pattern-table engine (`Poly/Grammar/`) — decode / match / print | Product tables inside the engine — DSL tables live in `DomainModeling`, C# tables in `Interpretation`; parallel pattern engines |
| **DomainModeling (DSL)** | One closed `.poly`; session binds `uses` concepts | Dialects; per-library token kinds; `Domain.ResolveHost` |
| **MCP** | Interactive harness + scratch store | Product doors; second evaluator; inferred `Main` |
| **Validation** | **Deleted** — constraints live under DomainModeling | Reintroducing a dormant rule surface |

**Deps:** Interpretation → Ast + Introspection. DomainModeling → Ast for pure lowering. Introspection ↛ Interpretation. V2 `Poly/Data/Modeling` is gone. Placement: [`AGENTS.md`](../AGENTS.md) Ops.

---

## 3. Critical machinery (use this)

If you need a parallel facility, stop.

### 3.1 Analysis + metadata

| Piece | Location |
|-------|----------|
| Framework | `Poly/Analysis/` — `AnalyzerBuilder`, `Analyzer`, `AnalysisContext`, `AnalysisResult` |
| Pass contract | `INodeAnalyzer` — post-order; registration order is the schedule |
| Facts | `IAnalysisMetadata` via `SetMetadata` / `GetMetadata<T>` |
| Semantic passes | `Poly/Interpretation/Analysis/` |
| Entry | `Interpreter.Analyze` / `Interpreter.Compile` (Compile fail-closed on errors) |

Facts live on nodes. Semantic consumers fail closed without `AnalysisResult`. Domain authoring/MCP/compile analyze through `DomainSession.Analyze`, which binds that session before the pipeline. An unbound domain resolves ids through `ExtensionCatalog.Core` and throws when that catalog cannot load one. Catalog first (`DomainCatalogPass`); later passes read it. Subscriptions: `SubscriptionDispatchPlanMetadata` on stage + entity — store and C# export consume the **same** plan. Quantifiers lower to `foreach` over collection nav (fail closed without store). Pass order: `Poly/Interpretation/Analysis/README.md`. Guide: `docs/interpretation/analysis-pass-guide.md`.

### 3.2 Node replacement

**The** rewrite mechanism: `context.SetNodeReplacement` / `GetNodeReplacement` (`Poly/Analysis/`). Passes do not mutate the tree; backends compile the replacement. Prefer an `INodeAnalyzer` over a product-local rewriter or emitter patch.

### 3.3 Direct AST → VM

| Piece | Location |
|-------|----------|
| Emitter | `Poly/Interpretation/Vm/Abi/AbiEmitter.cs` |
| Façade | `Poly/Interpretation/Interpreter.cs` |
| Runtime | `VmState`, `VmProgram`, … under `Poly/Interpretation/Vm/` |

No intermediate primitive IR. Keep the emitter a generic compiler of known nodes — fix upstream (lower / analyze / replace). Known members: `Ref` / `Ref<T>` (`Poly/Interpretation/Vm/Ref.cs`), not `typeof(T).GetMethod(...)`. `Await` executes by blocking on the operand. `Task` / `Task<T>` / `ValueTask` / `ValueTask<T>` run through `Ref`-bound helpers whose `GetResult` waits. Any other structurally awaitable type resolves `GetAwaiter`, `IsCompleted`, `OnCompleted` or `UnsafeOnCompleted`, and `GetResult` through the type provider, and blocks on the continuation when `IsCompleted` is false. Analysis types the result as `T` or void. An `IsAsync` AST method body yields a completed `Task<T>` (`Task.FromResult<T>`) or `Task` (`Task.CompletedTask`) at its return, so a caller's `Await` composes; a method that is not `IsAsync` is left alone. No state machine, no scheduler; the resume spine is for `SuspendNode`.

### 3.4 Domain lowering

| Piece | Location |
|-------|----------|
| Expressions | `DomainExpressionLoweringPass` (+ session `ExpressionMeaning` for libraries) |
| Effects | `EffectLoweringPass` — real nodes, not `null` |
| Module | `DomainProgramProjection.ToSyntax` — types + ops; no `Main` |
| Policy eval | `ModuleInstanceExecutor.EvaluatePolicy` → cached body → `Interpreter` |

Expand to **generic** Syntax (no domain opcodes). StageTransition / self-invoke / cross-entity / for-invoke / create / unique / clocks — shapes and residual debt: [`docs/interpretation/domain-execution-model.md`](interpretation/domain-execution-model.md). ADR: [`docs/decisions/2026-06-08-domain-lowering-boundary.md`](decisions/2026-06-08-domain-lowering-boundary.md). Named invoke runs module method bodies from `session.Lower` (same tree print uses). Do not add consumer-specific lowering flags or a second effect interpreter.

**Contract contributions.** A library's `Register` may call `SessionBuilder.AddContract(surface, bodies, targets)` to contribute one `ImportedContract` surface, optionally the body factory per endpoint, and the host targets the package is for. The slot is attributed to the registering library id, one contributor per contract name, and frozen as `DomainSession.ContractContributions`. `uses <id>` seeds the surface onto the resolved domain as an `ImportedContract` (`UsesId`, and `Pin` when the source wrote `uses <id> <pin>`); the body binds it. The surface's `Version` is the package's, never the pin. A body is a `Func<Block>` called once per splice site (fresh node ids; a body reused across binds fails closed) and is never run at Load. Lower splices a contributed body at its bind: the bound parameter must be named `request` and the body reads it as the `Parameter("request")` node (no `Variable` alias), then the body's statements, then its final `return <DomainResult>;` as the call outcome behind the existing failure guard. An in-solution `InternalDomain` bind with no body is erased and the local action still runs; an `ExternalProvider` endpoint with no body fails Lower. No `{Contract}Adapters` type is emitted. **Platform target:** the target is a host fact (`DomainSession.Target`, set by the compile call, never by `uses` and never a `Domain` field). A contribution that declares targets (`ContractContribution.Targets`, e.g. `posix`) is `DMCON004` when the host target is absent or not among them; the declared set is implementation metadata, not surface, so it is not in the fingerprint.

**Binding facts and the surface pin (#420).** `ContractIntegrationAnalyzer` publishes `ContractSurfaceMetadata` on each contract (library, source, version, fingerprint, pin, pin kind, matched) and `ContractBindingMetadata` on each bind (same surface identity, authored endpoint, resolved endpoint, action, parameter, status). The pin is exact: a 64-lowercase-hex pin is compared to the surface `Fingerprint`, anything else to `Version`; a mismatch is `DMCON010` on the contract and Lower refuses. No pin is the in-workspace case: no error, and the bag carries the resolved fingerprint. A bind may name the original action: candidates are matched by `ContractEndpoint.SourceName`, a payload type must accept the bound parameter (identity, or a narrower same-property-shape value type by `ConstraintMerge` subsumption), and the unique most-constrained accepting payload wins (`DMCON011`, a warning naming it) while incomparable candidates are `DMCON012`. A missing endpoint is `DMCON013`. Every action of the bound name on its entity must share one parameter list (name and type); a stage variant with another signature is an error, not a first-wins `Bound`. Lower reads the resolved `ContractBindingMetadata.Endpoint`, so print and simulate splice the same identity; the authored name is only the fallback for a hand-built domain lowered without analysis.

**Boundary constraints (#425, decision 21).** At a resolved bind, `ContractBoundaryCheck` publishes `ContractBoundaryMetadata` (precondition verdict, postcondition verdict, compatibility) beside the binding fact. The caller's argument must sit inside the endpoint's public payload property constraints and the preconditions that reduce to a `Property cmp literal` comparison: a proof is silence, a disjoint pair is `DMCON014` and Lower refuses, and a merge that cannot decide (`ConstraintMerge.MergeResult` `Unmergeable`, or a caller wider than the obligation) is `DMCON015` and does not block Lower. A precondition naming a property the argument does not carry is `DMCON015` too, never a violation; failing it would reject a legal call. A same-shape constraint mismatch is decided here instead of the generic incompatible error, so a wider caller is unknown rather than refused; a shape mismatch stays `Incompatible`. Library bodies are not walked (Scot, 2026-11-08): every contributed body is `Unknown` on the postcondition half with one `DMCON015`, and a surface-only contribution records the same verdict silently. The note is not in the fingerprint, is not a pin input, and does not waive `DMCON010`.

**Constraint contributions.** A library may register an `IConstraintCompiler`; core places its AST at a semantic anchor (`ConstraintAnchor`), inserts contributed parameters before the method's first optional parameter, and threads them along the lowered call set. The storage libraries own `unique`: the guard calls the in-process `IStoragePort` (`Exists` = pending key or database), and the post-construction call records the pending key. The port is read-only — no writes and no `SaveChanges` — and is implemented by the generated `{Domain}DbContext` and by the interpreter host's session port. The module never names a host context type, and there is no second persistence runtime.

**Stage ledger and constraint coverage.** `SessionBuilder` records one `StageContribution` per library and pipeline stage it enters: Load in `Register`, Analyze when the registering library adds an analyzer, Lower when it adds a constraint compiler. `DomainSession.Analyze`, `Lower`, `Emit` and the internal `Compile` refuse a session whose core library is not loaded at Load. Registration attributes an analyzer or compiler to the library whose `Register` is running; a registration made outside a library load records no stage row. Constraint check coverage is `ConstraintCheckWriter.CoverageByConstraint`: each `Constraint` record names the writer kinds it can emit or the owner that writes its guard (or, with a `marker:` prefix, why no guard exists; `unique` is storage-owned), and `BuiltinConstraintCompiler` claims exactly the rows with kinds. Same-type constraints combine through `ConstraintMerge.MergeResult` (`Net | Unmergeable | Empty`): `Empty` is the only unsatisfiable verdict, an unregistered kind is `Unmergeable`, and identical `unique`/`default` are idempotent; `Merge` is the net-or-null convenience for callers that do not need the difference. `AddConstraintToPropertyChange` treats an identical constraint as a no-op, so a duplicate never reaches Lower as a second guard.

**Diagnostic allowance.** There is no contradiction gate. A reader-found copy is an `ObservedFlow` (`SessionBuilder.WithObservedFlows`, frozen on `DomainSession.ObservedFlows`) checked by the existing assign checks: `EffectAnalyzer` calls `IsConstraintSubsumed` for length and range (`DMEFF008` on the target property) and reports an optional source flowing into a required target (`DMEFF014` on the target property, #439), and `ExpressionTypeAnalyzer` calls `CheckCompatible` (`DMSEM003` on the target property); an unresolvable or self path is `DMSEM005` on the domain. Nothing is inserted into the tree. Unacknowledged flow diagnostics are errors and block through `ThrowIfHasErrors`, the only gate. A `DiagnosticAllowance` (`SessionBuilder.WithDiagnosticAllowances`, frozen on `DomainSession.DiagnosticAllowances`) names a diagnostic code and the finding's element path (`Domain/Entity/Property`) plus non-blank provenance ids; a match downgrades only that flow diagnostic to a warning, which stays in the report and does not stop Lower. An allowance no flow diagnostic consumed (wrong code or path, stale after the flow became compatible, blank provenance) is the `DMALW001` error. `WithDomain` copies both lists on both branches. ADR: [`docs/decisions/2026-11-08-diagnostic-allowance.md`](decisions/2026-11-08-diagnostic-allowance.md).

**Instance handles, not runtime types.** `ModuleInstance` (`Runtime/ModuleInstance.cs`) is a public record of `(Id, EntityName, Bag)` — the executor's handle onto the dictionary the interpreter already runs. It carries no behaviour: no methods, no dispatch, no lifecycle. That is the line the tenet draws. "There is no instance runtime type" means no second object model with its own semantics; a bag handle is the host's index into the module's dictionary and is not one.

### 3.5 Introspection

Host-neutral types/members (`Poly/Introspection/`); CLR is first provider. Consumers use `ITypeDefinition` / providers — not ad-hoc reflection or a second registry. Adapt missing shapes via analysis replacement, not emitter special cases. Detail: `Poly/Introspection/README.md`, `docs/technical/introspection.md`.

### 3.6 MCP and extensions

| Piece | Location |
|-------|----------|
| Tools / sessions | `Poly.Mcp/` |
| Compile session | `DomainSession` in `Poly/DomainModeling/` (MCP holds it) |
| Libraries | `IDomainLibrary` — `uses` loads analyzers/maps/contributors |

Simulate = Interpreter on lowered module bodies with caller-supplied context — not Domain/Effect IR. Extensions: one `.poly` language; session loads `uses` ids (unknown/duplicate fail closed). Persistence/HTTP emit doors only when listed. Core seed does not emit `Program.cs`.

### 3.7 Debugging

`VmState.DebugInterrupt` · [`docs/decisions/2026-06-08-breakpoint-architecture.md`](decisions/2026-06-08-breakpoint-architecture.md) · `docs/interpretation/debugging-and-tracing.md`.

---

## 4. Stop inventing this — use that

| Need | Use | Do not invent |
|------|-----|---------------|
| Known `MethodInfo` / ctor | `Ref` / `Ref<T>` | `typeof(T).GetMethod(...)` |
| Rewrite AST | `SetNodeReplacement` / `INodeAnalyzer` | Product rewriter; emitter patches |
| Facts about a node | `IAnalysisMetadata` | Side tables |
| Types / members | `ITypeDefinitionProvider` | Second type registry |
| Run program / policy / action | `Interpreter` on lowered operation AST | Effect-IR execute; second evaluator; `Comment` as success |
| Conversation simulate | MCP + caller context + same AST | Infer `Main`; MCP as product API |
| Product door | Opt-in `uses` + `IArtifactContributor` | Core `Program.cs` |
| Domain mutation | `DomainEvolution`…`Apply` | In-place graph edits |
| New domain feature | Lower to existing Syntax (+ analyze/replace) | Domain opcodes; ABI one-offs |
| Why / multi-step work | `docs/decisions/` · `docs/plans/` | Expanding CORE or AGENTS into a plan |

---

## 5. Doc map

| Need | Open |
|------|------|
| Always-on tenets / target | [`AGENTS.md`](../AGENTS.md) |
| Frozen architecture | This file §0 · [`decisions/2026-09-04-frozen-core-pipeline.md`](decisions/2026-09-04-frozen-core-pipeline.md) |
| Lowered module = domain meaning | [`decisions/2026-09-05-lowered-module-is-domain-meaning.md`](decisions/2026-09-05-lowered-module-is-domain-meaning.md) · always-on Agent target |
| Principles (Rule + How) | [`decisions/2026-core-engineering-principles.md`](decisions/2026-core-engineering-principles.md) |
| Trust bar | [`decisions/2026-07-11-platform-trust-bar-and-dogfood.md`](decisions/2026-07-11-platform-trust-bar-and-dogfood.md) |
| Domain library / MCP harness | [`decisions/2026-08-15-domain-library-extensions-mcp-harness.md`](decisions/2026-08-15-domain-library-extensions-mcp-harness.md) |
| Lower vision | [`plans/reference/session-lower-plan-2026-09-21.md`](plans/reference/session-lower-plan-2026-09-21.md) |
| Queue | Open GitHub issues ([umbrella #305](https://github.com/scoizzle/Poly/issues/305) is closed; `docs/plans/poly-eng/board.md` is retired) |
| Module detail | `Poly/*/README.md`, `docs/interpretation/*` |

---

## 6. Self-check before ship

1. Composed §3 instead of a parallel facility?  
2. Stayed inside §2 ownership?  
3. New shape via lower / analyze / **replace** — not emitter/ABI patch?  
4. New DSL/effect surface lowers completely (shipped ⊆ lowerable)?  
5. New process door is opt-in `uses`?  
6. MCP change is harness on the same AST?  
7. Updated this file if a listed mechanism changed?  
8. Respected frozen core / no new consumer-specific lowering flag?  
9. Build + tests green (`AGENTS.md` Ops)?
