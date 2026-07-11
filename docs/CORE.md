# Poly Core Reference

**Audience:** Agents and humans changing platform code.  
**Job:** Purpose, boundaries, and **existing machinery you must not reinvent**.  
**Not this doc:** Execution plans (`docs/plans/`), decision history (`docs/decisions/`), pass-writing tutorials.

**Load rule:** Read this end-to-end before changing `Syntax`, `Interpretation`, `Introspection`, `DomainModeling`, or `Poly.Mcp`. Keep it short — if a change needs more prose here, link out instead of growing this file.

**Maintenance:** Update this file in the same change that alters a listed mechanism. Stale CORE is worse than no CORE.

**Deferred packaging:** A future rename may split today’s `Poly.Syntax` into `Poly.Ast` + `Poly.Analysis` (nodes vs analysis framework). See [`docs/plans/poly-ast-analysis-module-split.md`](plans/poly-ast-analysis-module-split.md) — **do not execute** while other platform work is in flight. Until then, paths in this file remain `Syntax` / `Syntax/Analysis`.

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
| Domain lowers to **generic** ops | No domain-specific VM opcodes (`CheckPolicy`, etc.) |
| Immutability at domain boundary | Mutate via `DomainEvolution`…`Apply`, not by editing graphs in place |
| Smallest coherent platform | Prefer using an existing mechanism over inventing a parallel one |

TFM: `net10.0`, nullable on, zero external dependencies in core `Poly/`.

---

## 2. Separation of concerns

| Concern | Owns | Must not |
|---------|------|----------|
| **Syntax** | `Node` records, `NodeId`, analysis framework (`Analyzer`, `AnalysisContext`, metadata store, **node replacement**) | Execution semantics, domain shapes, MCP session |
| **Interpretation** | Semantic passes, `Interpreter`, `DirectVmAbiEmitter`, VM runtime | Domain concepts, free-form subject bags as product API |
| **Introspection** | Platform-agnostic type/member model so Interpretation can **simulate any reasonable type system on any reasonable platform**; CLR is the first provider | Depending on Interpretation; baking one host (e.g. CLR-only APIs) into the core contract; product hacks for one consumer |
| **DomainModeling** | Immutable `Domain`, evolution, `DomainExpression`, lower-to-AST | Domain VM opcodes; inventing a second AST rewriter stack |
| **Validation** | `Rule` / `RuleSet` evaluation surface | Owning the AST or VM |
| **MCP (`Poly.Mcp`)** | Session store, tools, tool honesty | Domain mutation semantics; claiming capabilities the core does not have |
| **Synthesis** | Macros (VM validates) | Reverse deps from Interpretation |

**Enforced dependency direction (core):**

- `Interpretation` → `Syntax`, `Introspection`
- `DomainModeling` → `Syntax` for pure lowering (`DomainExpressionLoweringPass`)
- `PolicyEvaluator` (under DomainModeling) **bridges** to Interpretation/VM for evaluate/compile — that is intentional consumption of the platform, not a license to fork the ABI
- `Introspection` ↛ `Interpretation`
- V2 `Poly/Data/Modeling` is **deleted** — only V3 DomainModeling remains

**Placement (where new code goes):** see Placement Rules in `AGENTS.md`. Name types for **what they are**, not which GoF pattern they resemble.

---

## 3. Critical system support

Use these. If you think you need a parallel facility, stop and re-read this section.

### 3.1 Analysis pipeline + metadata

| Piece | Location |
|-------|----------|
| Framework | `Poly/Syntax/Analysis/` — `AnalyzerBuilder`, `Analyzer`, `AnalysisContext`, `AnalysisResult`, `NodeMetadataStore` |
| Pass contract | `INodeAnalyzer` — post-order walk, `TryBeginAnalyzerVisit`, dependencies |
| Facts on nodes | `IAnalysisMetadata` via `context.SetMetadata` / `GetMetadata<T>` |
| Semantic passes | `Poly/Interpretation/Analysis/` (types, scopes, CFG, side effects, folding, …) |
| Standard entry | `Interpreter.Analyze` / `Interpreter.Compile` (cached full pass list) |

**Do not:** attach parallel side tables keyed by string; re-walk the whole tree outside the pass model for product features that belong in analysis.

Pass order and registry: `Poly/Interpretation/Analysis/README.md`. Authoring guide: `docs/interpretation/analysis-pass-guide.md`.

### 3.2 Node replacement (AST rewrite support)

**This is the platform rewrite mechanism.** Prefer it over hand-rolled tree rewriters.

| | |
|--|--|
| **API** | `context.SetNodeReplacement(node, replacement)` / `provider.GetNodeReplacement(node)` |
| **Impl** | `Poly/Syntax/Analysis/NodeReplacementMetadata.cs` (metadata; AST nodes stay immutable) |
| **Producer example** | `ConstantFoldingPass` — folds/simplifies, then `SetNodeReplacement` |
| **Consumers** | `DirectVmAbiEmitter.CompileNode` (honors replacement before dispatch); `LinqExpressionGenerator` likewise |

**Contract**

1. Passes **do not mutate** the original tree. They register `original → replacement` in analysis metadata.
2. Backends consult replacement **at compile time** and compile the replacement node.
3. Use for desugaring, constant fold, algebraic simplify, and other rewrites the analysis/backend pipeline already understands.
4. **Do not** invent a product-local `*Rewriter` that walks `Node.Children` and reimplements half of Syntax unless you have an explicit decision that node replacement cannot express the need — and even then, prefer a real analysis pass that sets replacements.

**Not a use case:** “make `Dictionary` look like a property bag for policies.” That is a **subject-model** problem (typed CLR properties / records), not a missing rewriter in DomainModeling or a VM fallback.

### 3.3 Direct AST → VM ABI

| Piece | Location |
|-------|----------|
| Emitter | `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs` |
| Façade | `Poly/Interpretation/Interpreter.cs` |
| Runtime | `VmState`, `VmProgram`, heap/ring ABI under `Poly/Interpretation/Vm/` |

No intermediate primitive flattening step. Analysis metadata + AST are the inputs. Do not add product-only resolution fallbacks in the emitter for one DomainModeling scenario — fix types/resolution/subjects upstream.

### 3.4 Domain expression lowering

| Piece | Location |
|-------|----------|
| Lower DE → Syntax AST | `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs` |
| Policy compile/eval | `Poly/DomainModeling/Lowering/PolicyEvaluator.cs` — **VM-primary**; LINQ via dual-oracle tests |
| Domain change | `DomainEvolution`…`Apply()` with analysis gate + rollback |

Domain concepts expand to **generic** Syntax nodes (`Member`, `And`, `Equal`, …), not new opcodes. ADR: `docs/decisions/2026-06-08-domain-lowering-boundary.md`.

### 3.5 Introspection (types and members)

**Goal:** Enable **Interpretation to simulate programs against any reasonable type system from any reasonable platform** — not “wrap .NET reflection forever.” The core abstractions describe types and members in host-neutral terms; concrete platforms plug in as **providers**. Today’s working provider is the CLR; future providers (other runtimes, domain/AST type systems, foreign ABIs) must fit the same contracts without rewriting analysis or the VM façade.

**Job today:** Resolve types and members for analysis and backends **without** coupling every consumer to raw CLR reflection. Passes and emitters ask Introspection; the CLR is the first implementation, not the model.

| Piece | Location |
|-------|----------|
| Abstractions | `Poly/Introspection/` — `ITypeDefinition`, `ITypeMember` / `ITypeProperty` / `ITypeMethod` / `ITypeField` / `ITypeConstructor`, `IParameter` |
| Providers | `ITypeDefinitionProvider`, `TypeDefinitionProviderCollection` (LIFO stack: last added wins, then fallthrough) |
| CLR bridge (first host) | `Poly/Introspection/CommonLanguageRuntime/` — `ClrTypeDefinitionRegistry.Shared`, `ClrTypeDefinition`, method/property/field wrappers |
| Host runtime type without polluting core | `IClrType` + `TypeDefinitionRuntimeTypeExtensions` — **not** `System.Type` (or any host type) on every `ITypeDefinition` |
| Wired into analysis | `AnalysisContext.TypeDefinitions` — default `ClrTypeDefinitionRegistry.Shared` (`Poly/Syntax/Analysis/AnalysisContext.cs`, `Analyzer`) |
| Consumer pass | `TypeAndMemberResolutionPass` (Interpretation) stamps resolved type/member metadata from the provider |

**Contract**

1. **Host-neutral core, host-specific adapters.** Interpretation and other consumers depend on `ITypeDefinition` / `ITypeDefinitionProvider`, not on CLR reflection APIs. New platforms add providers under Introspection (or a sibling adapter folder), not forks of the resolver or emitter.
2. **Provider-agnostic consumers.** Passes and backends resolve via `context.TypeDefinitions.GetTypeDefinition(...)` / member queries on `ITypeDefinition` — not ad-hoc `typeof` + reflection sprinkled through product code.
3. **Compose providers** with `TypeDefinitionProviderCollection` when you need domain/AST types **plus** a host — do not fork a second type registry inside DomainModeling or MCP.
4. **Introspection ↛ Interpretation.** Adapters stay under Introspection; semantic passes and execution stay under Interpretation. No reverse dependency.
5. **Member shape matters.** `Member` / `Invoke` need properties and methods the **active provider** can expose. Bag types without property members are a **subject-model** problem (see §3.6), not a missing `get_Item` special case in `ClrTypeDefinition` or an emitter reflection fallback.
6. **Dormant API surface is intentional.** Read/Write/Initialize delegates, synthetic indexers, and broad `TypeCategory` flags complete a multi-host-capable type model; do not delete them as “unused” without a decision (see `docs/technical/introspection.md`).

**Do not:** special-case method filtering or manual `GetMethods` in the emitter for one DomainModeling scenario; invent a parallel type dictionary keyed by string; put `System.Type` (or any single-host type handle) on the core `ITypeDefinition` interface; treat CLR-only shortcuts as the long-term Interpretation contract.

Module README: `Poly/Introspection/README.md`. Audit notes: `docs/technical/introspection.md`.

### 3.6 Policy / runtime subjects (product path)

| Allowed | Forbidden as product subjects |
|---------|-------------------------------|
| Types with real CLR properties (records, POCOs) | Raw `Dictionary<string, object>`, `ExpandoObject`, dynamic bags as `Member` targets |
| Non-null value-type properties (or explicit defaults) | Null nullables that blow up VM unbox |

Helpers for samples/invariants belong in DomainModeling (or a thin MCP mapper) — **typed bags**, not “rewrite Member to get_Item and teach the emitter Dictionary.”

### 3.7 MCP

| Piece | Location |
|-------|----------|
| Tools / session | `Poly.Mcp/Tools/`, `Poly.Mcp/Sessions/` |

MCP is a **thin adapter**: session identity, tool surface, honest capability claims. It must not invent domain semantics. Prefer model-optimized DomainModeling APIs; MCP wraps them. Tools that only expose AST/expression must not be named as if they evaluate policies on subjects.

### 3.8 Debugging / tracing (VM)

- Breakpoints: `VmState.DebugInterrupt` (external policy) — `docs/decisions/2026-06-08-breakpoint-architecture.md`
- Module READMEs under `Poly/Interpretation/Vm/` and `docs/interpretation/debugging-and-tracing.md`

---

## 4. Stop inventing this — use that

| Need | Use | Do **not** invent |
|------|-----|-------------------|
| Rewrite AST for backends | `SetNodeReplacement` / analysis pass | Product-local full-tree `*Rewriter` |
| Facts about a node | `IAnalysisMetadata` on `AnalysisContext` | Parallel dictionaries / free-form bags |
| Resolve types / members (any host) | `ITypeDefinitionProvider` + `AnalysisContext.TypeDefinitions` | Ad-hoc reflection; second type registry; emitter `GetMethods` fallbacks; CLR as the only allowed model |
| Stack host + custom types | `TypeDefinitionProviderCollection` | Hard-coding only `typeof` / one runtime in product modules |
| Run expression/policy | `Interpreter` / `PolicyEvaluator` (VM) | Second evaluator framework |
| Domain mutation | `DomainEvolution`…`Apply` | In-place graph edits; resurrecting V2 |
| Policy subject shape | Typed CLR properties | Dict/Expando as `Member` subjects + ABI hacks |
| Domain feature in VM | Lower to existing Syntax/ops | Domain-specific opcodes or emitter special cases |
| Cross-cutting “why” | `docs/decisions/` | Re-litigating in code comments or drive-by PRs |
| Multi-step work | `docs/plans/` (task files) | Expanding CORE or AGENTS into a plan |

---

## 5. Doc map (what to open next)

| Need | Open |
|------|------|
| Principles (values) | `AGENTS.md` + `docs/decisions/2026-core-engineering-principles.md` |
| Why of a major choice | `docs/decisions/README.md` |
| Active execution work | `docs/plans/v2-to-v3/master-roadmap.md` |
| Module detail | `Poly/*/README.md`, `docs/interpretation/*` |
| Introspection detail | `Poly/Introspection/README.md`, `docs/technical/introspection.md` |
| Historical / may be stale | `docs/ARCHITECTURE.md` — prefer this file + module READMEs for truth |

---

## 6. Quick self-check before you ship

1. Did I use an existing mechanism from §3 instead of a parallel one?  
2. Did I stay inside the ownership table in §2?  
3. If I touched ABI/emitter/introspection for a DomainModeling convenience, did I have a real platform reason?  
4. Do docs (this file if mechanisms changed) still match the code?  
5. Build/tests green (`AGENTS.md` Build & Test).
