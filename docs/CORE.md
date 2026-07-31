# Poly Core Reference

**Audience:** Agents and humans changing platform code.  
**Job:** Purpose, boundaries, and **existing machinery you must not reinvent**.  
**Not this doc:** Execution plans (`docs/plans/`), decision history (`docs/decisions/`), product recipes, pass-writing tutorials.

**Load rule:** Read this end-to-end before changing `Poly.Ast`, `Poly.Analysis`, `Interpretation`, `Introspection`, `DomainModeling`, or `Poly.Mcp`. Keep it short — if a change needs more prose here, link out instead of growing this file.

**Maintenance:** Update this file in the same change that alters a listed mechanism. Stale CORE is worse than no CORE.

**Module split:** `Poly.Syntax` has been split into `Poly.Ast` (node records, NodeId, fluent API) + `Poly.Analysis` (analysis framework, metadata, node replacement). See [`docs/plans/poly-ast-analysis-module-split.md`](plans/poly-ast-analysis-module-split.md) (completed 2026-07-26). Paths in this file use the new layout.

---

## 1. Purpose

Poly is a **neurosymbolic platform**: domain models and policies are authored as structured data, lowered to a **symbolic AST**, analyzed, then executed by the **VM** (canonical semantics). The goal is shipped, correct end-to-end behavior — not framework completeness.

```text
Domain (immutable records)
  → DomainExpression / evolution Apply
  → Syntax AST          ← primary symbolic / serializable IR
  → Analysis            ← metadata + diagnostics + optional node replacement
  → DirectVmAbiEmitter  ← direct AST → VM ABI (no intermediate primitive IR)
  → VM (VmState)        ← canonical execution
```

**Hard lines**

| Truth | Implication |
|-------|-------------|
| AST is the symbolic primary | Do not reintroduce a parallel “primitive IR” for product paths |
| VM is canonical execution | LINQ path is secondary (oracle / reference), not a second product engine |
| Domain lowers to **generic** ops | No domain-specific VM opcodes |
| Extend the platform **in the pipeline** | New meaning: lower to existing nodes, analyze, and/or **replace nodes** — not special-case the emitter, ABI, or one host’s type filter |
| Analysis is required for downstream semantics | Domain/runtime/tooling paths that resolve semantic meaning must consume an `AnalysisResult`; no semantic execution path without analysis |
| One coherent path | Prefer composing existing mechanisms over a parallel rewriter, evaluator, or type registry |
| Immutability at domain boundary | Mutate via `DomainEvolution`…`Apply`, not by editing graphs in place |
| Smallest coherent platform | Prefer using an existing mechanism over inventing a parallel one |

TFM: `net10.0`, nullable on, zero external dependencies in core `Poly/`.

---

## 2. Separation of concerns

| Concern | Owns | Must not |
|---------|------|----------|
| **Ast** | `Node` records, `NodeId`, fluent construction API | Execution semantics, domain shapes, analysis logic |
| **Analysis** | Analysis framework (`Analyzer`, `AnalysisContext`, metadata store, **node replacement**) | Execution semantics, domain concepts, MCP session |
| **Interpretation** | Semantic passes, `Interpreter`, `DirectVmAbiEmitter`, VM runtime | Domain concepts; one-off ABI/emitter forks for a single consumer |
| **Introspection** | Platform-agnostic type/member model so Interpretation can **simulate any reasonable type system on any reasonable platform**; CLR is the first provider | Depending on Interpretation; baking one host into the core contract |
| **DomainModeling** | Immutable `Domain`, evolution, `DomainExpression`, lower-to-AST; **stage transitions are the authorable observable** (no event/publish/subscribe surface — see `docs/decisions/2026-07-17-stage-transition-as-observable.md`) | Domain VM opcodes; tree rewrites outside analysis + node replacement |
| **Validation** | `Rule` / `RuleSet` evaluation surface | Owning the AST or VM |
| **MCP (`Poly.Mcp`)** | Session store, tools, tool honesty | Domain mutation semantics; claiming capabilities the core does not have |
| **Synthesis** | Macros (VM validates) | Reverse deps from Interpretation |

**Enforced dependency direction (core):**

- `Interpretation` → `Syntax`, `Introspection`
- `DomainModeling` → `Syntax` for pure lowering (`DomainExpressionLoweringPass`). Execution model: [`docs/interpretation/domain-execution-model.md`](interpretation/domain-execution-model.md).
- `PolicyEvaluator` (under DomainModeling) **bridges** to Interpretation/VM for evaluate/compile — that is intentional consumption of the platform, not a license to fork the ABI
- `Introspection` ↛ `Interpretation`
- V2 `Poly/Data/Modeling` is **deleted** — only **DomainModeling** remains (legacy “V3” label = current stack; rename plan: [`plans/post-v2-delete-naming-cleanup.md`](plans/post-v2-delete-naming-cleanup.md))

**Placement (where new code goes):** see Placement Rules in `AGENTS.md`. Name types for **what they are**, not which GoF pattern they resemble.

---

## 3. Critical system support

Use these. If you think you need a parallel facility, stop and re-read this section.

### 3.1 Analysis pipeline + metadata

| Piece | Location |
|-------|----------|
| Framework | `Poly/Analysis/` — `AnalyzerBuilder`, `Analyzer`, `AnalysisContext`, `AnalysisResult`, `NodeMetadataStore` |
| Pass contract | `INodeAnalyzer` — post-order walk, `TryBeginAnalyzerVisit`, dependencies |
| Facts on nodes | `IAnalysisMetadata` via `context.SetMetadata` / `GetMetadata<T>` |
| Semantic passes | `Poly/Interpretation/Analysis/` (types, scopes, CFG, side effects, folding, …) |
| Standard entry | `Interpreter.Analyze` / `Interpreter.Compile` (cached full pass list) |

**Principle:** Facts about a program live on nodes via analysis metadata. Do not attach parallel side tables or re-walk the tree outside the pass model for work that belongs in a pass.

**Contract:** For downstream consumers that answer semantic questions (lowering decisions, runtime semantic dispatch, MCP semantic inspection, compiler semantic mapping), analysis is non-optional. Those paths must fail closed when `AnalysisResult` or required metadata is missing.

**Domain analysis future state:** target pipeline (validate · catalog · derive), single catalog, consumer contracts, export boundary — [`docs/plans/domain-analysis-future-state.md`](plans/domain-analysis-future-state.md).  
**Present inventory / cutover notes:** [`docs/plans/domain-analysis-simplification.md`](plans/domain-analysis-simplification.md). Syntax IR projection is an **export** step on a finished `AnalysisResult`, not a domain-fact analysis pass.

**Domain catalog (DAS W1):** `DomainCatalogPass` is the sole product publisher of name→member maps (`DomainCatalogMetadata` on the domain node). Product lookups go through `DomainSemanticLookupExtensions` (catalog-only when domain-keyed). Intermediate Semantic DTLM/RLM still publish for mid-pipeline analyzers and are embedded in the catalog; entity-keyed ARM and domain-keyed MTI dual-write are retired. Remaining non-catalog bags (relationship contracts, stage subscription plans, entity structure, capabilities): ownership matrix in [`docs/plans/das-catalog-design.md`](plans/das-catalog-design.md).

**Effective stage surface (DAS W2):** `CapabilityAnalyzer` publishes the canonical stage-effective view (`StageCapabilityMetadata`). Composition is one algorithm in `DomainEffectiveSurface` — policies = entity + stage (not action policies); actions = stage-local only. Product paths use `GetEffectivePolicies` / `GetEffectiveActions`; MCP `DescribeStage` uses those helpers. `BehaviorPass` is a pack DTO adapter over Capability + action-level EPM (no third composition path).

**Fact vs validate packs (DAS W3.2):** Small fact emitters publish bags consumers read; megapass diagnostics stay in validate packs. Template split: `RequiredPropertiesPass` → `RequiredPropertiesMetadata`; `EffectFactsPass` → `ResolvedRelationshipTargetMetadata` on create-in. `PolicyConstraintAnalyzer` / `EffectAnalyzer` are lint-only (no fact publication). Further thinning of remaining diagnostic LOC is follow-up — see `docs/plans/simple-agent-tasks/das-w3-2-split-validation-facts.md`.

Pass order and registry: `Poly/Interpretation/Analysis/README.md`. Authoring guide: `docs/interpretation/analysis-pass-guide.md`.

### 3.2 Node replacement (AST rewrite support)

**This is the platform rewrite mechanism.** Prefer it over hand-rolled tree rewriters and over backend special cases.

| | |
|--|--|
| **API** | `context.SetNodeReplacement(node, replacement)` / `provider.GetNodeReplacement(node)` |
| **Impl** | `Poly/Analysis/NodeReplacementMetadata.cs` (metadata; AST nodes stay immutable) |
| **Producer example** | `ConstantFoldingPass` — folds/simplifies, then `SetNodeReplacement` |
| **Consumers** | `DirectVmAbiEmitter.CompileNode` (honors replacement before dispatch); `LinqExpressionGenerator` likewise |

**Principles**

1. Passes **do not mutate** the original tree; they register `original → replacement` in analysis metadata.
2. Backends compile the **replacement** (ordinary Syntax nodes) — the rewrite stays in analysis, not in the emitter.
3. Desugar, simplify, and adapt shapes **here** so Introspection and the ABI stay generic and multi-host.
4. Prefer an **`INodeAnalyzer`** that sets replacements over a product-local `*Rewriter` outside the pipeline.

### 3.3 Direct AST → VM ABI

| Piece | Location |
|-------|----------|
| Emitter | `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs` |
| Façade | `Poly/Interpretation/Interpreter.cs` |
| Runtime | `VmState`, `VmProgram`, heap/ring ABI under `Poly/Interpretation/Vm/` |

No intermediate primitive flattening step. Inputs are the AST plus analysis metadata (including replacements). **Principle:** keep the emitter a generic compiler of known nodes — fix upstream (lower / analyze / replace), do not patch the ABI for one scenario.

### 3.4 Domain expression lowering

| Piece | Location |
|-------|----------|
| Lower DE → Syntax AST | `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs` |
| Policy compile/eval | `Poly/DomainModeling/Lowering/PolicyEvaluator.cs` — **VM-primary**; LINQ via dual-oracle tests |
| Domain change | `DomainEvolution`…`Apply()` with analysis gate + rollback |

Domain concepts expand to **generic** Syntax nodes, not new opcodes. ADR: `docs/decisions/2026-06-08-domain-lowering-boundary.md`.

### 3.5 Introspection (types and members)

**Goal:** Enable **Interpretation to simulate programs against any reasonable type system from any reasonable platform** — not “wrap .NET reflection forever.” Host-neutral abstractions; platforms plug in as **providers**. CLR is the first implementation, not the model.

| Piece | Location |
|-------|----------|
| Abstractions | `Poly/Introspection/` — `ITypeDefinition`, member interfaces, `IParameter` |
| Providers | `ITypeDefinitionProvider`, `TypeDefinitionProviderCollection` (LIFO stack) |
| CLR bridge (first host) | `Poly/Introspection/CommonLanguageRuntime/` — `ClrTypeDefinitionRegistry.Shared`, … |
| Host runtime type without polluting core | `IClrType` + extensions — **not** a host type handle on every `ITypeDefinition` |
| Wired into analysis | `AnalysisContext.TypeDefinitions` (default CLR shared registry) |
| Consumer pass | `TypeAndMemberResolutionPass` stamps resolved type/member metadata |

**Principles**

1. Consumers depend on `ITypeDefinition` / providers, not on one runtime’s reflection API.
2. Compose providers; do not fork a second type registry in product modules.
3. Introspection ↛ Interpretation.
4. When a shape is not naturally visible as members, **adapt in analysis** (replace nodes) rather than special-casing a host adapter or the emitter for one consumer.
5. Dormant API surface that completes the multi-host model is intentional — see `docs/technical/introspection.md`.

Module README: `Poly/Introspection/README.md`.

### 3.6 MCP

| Piece | Location |
|-------|----------|
| Tools / session | `Poly.Mcp/Tools/`, `Poly.Mcp/Sessions/` |

**Principle:** thin adapter — session, tools, honest capability claims. Wraps DomainModeling; does not invent domain or execution semantics.

### 3.7 Debugging / tracing (VM)

- Breakpoints: `VmState.DebugInterrupt` — `docs/decisions/2026-06-08-breakpoint-architecture.md`
- `Poly/Interpretation/Vm/`, `docs/interpretation/debugging-and-tracing.md`

---

## 4. Stop inventing this — use that

| Need | Use | Do **not** invent |
|------|-----|-------------------|
| Rewrite or desugar AST | `SetNodeReplacement` / `INodeAnalyzer` | Product-local full-tree rewriter; emitter patches |
| Facts about a node | `IAnalysisMetadata` on `AnalysisContext` | Parallel side tables outside the metadata store |
| Resolve types / members | `ITypeDefinitionProvider` + `AnalysisContext.TypeDefinitions` | Ad-hoc reflection; second type registry; emitter method-lookup fallbacks |
| Stack host + custom types | `TypeDefinitionProviderCollection` | Hard-coding a single runtime into product modules |
| Run a program / policy | `Interpreter` / `PolicyEvaluator` after analyze | Second evaluator framework |
| Domain mutation | `DomainEvolution`…`Apply` | In-place graph edits; resurrecting V2 |
| Domain feature at runtime | Lower to existing Syntax ops (+ analyze/replace) | Domain opcodes; ABI special cases for one feature |
| Cross-cutting “why” | `docs/decisions/` | Re-litigating in drive-by comments |
| Multi-step work | `docs/plans/` | Expanding CORE or AGENTS into a plan |

---

## 5. Doc map (what to open next)

| Need | Open |
|------|------|
| Principles (values) | `AGENTS.md` + `docs/decisions/2026-core-engineering-principles.md` |
| Trust bar + first-customer strategy (T1–T3; product via domain + modules) | [`docs/decisions/2026-07-11-platform-trust-bar-and-dogfood.md`](decisions/2026-07-11-platform-trust-bar-and-dogfood.md) |
| Why of a major choice | `docs/decisions/README.md` |
| Active execution work | `docs/plans/v2-to-v3/simple-agent-tasks/vs-README.md` (post-M2 picks); `docs/plans/v2-to-v3/vertical-slice-finish-plan.md` (status) |
| Module detail | `Poly/*/README.md`, `docs/interpretation/*` |
| Introspection detail | `Poly/Introspection/README.md`, `docs/technical/introspection.md` |
| Historical / may be stale | `docs/ARCHITECTURE.md` — prefer this file + module READMEs for truth |

---

## 6. Quick self-check before you ship

1. Did I compose an existing mechanism from §3 instead of a parallel one?  
2. Did I stay inside the ownership table in §2?  
3. If I needed a new shape or rewrite, did I lower / analyze / **replace nodes** rather than special-case the emitter, ABI, or a host type filter?  
4. Do docs (this file if mechanisms changed) still match the code?  
5. Build/tests green (`AGENTS.md` Build & Test).
