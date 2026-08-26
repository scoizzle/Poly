# Poly Core Reference

**Audience:** Agents and humans changing platform code.  
**Job:** Purpose, boundaries, and **existing machinery you must not reinvent**.  
**Not this doc:** Execution plans (`docs/plans/`), decision history (`docs/decisions/`), product recipes, pass-writing tutorials.

**Load rule:** Read this end-to-end before changing `Poly.Ast`, `Poly.Analysis`, `Interpretation`, `Introspection`, `DomainModeling`, or `Poly.Mcp`. Keep it short — if a change needs more prose here, link out instead of growing this file.

**Maintenance:** Update this file in the same change that alters a listed mechanism. Stale CORE is worse than no CORE.

**Module split:** `Poly.Syntax` has been split into `Poly.Ast` (node records, NodeId, fluent API) + `Poly.Analysis` (analysis framework, metadata, node replacement). See [`docs/plans/poly-ast-analysis-module-split.md`](plans/poly-ast-analysis-module-split.md) (completed 2026-07-26). Paths in this file use the new layout.

---

## 1. Purpose

Poly is a **neurosymbolic platform**: domain models and policies are authored as structured data, lowered to a **symbolic AST**, analyzed, then executed by the **VM** (canonical semantics). A domain is a **library of legal operations**, not a process with a required `Main`. Known algorithms are a second generation path on the same AST → VM spine. The goal is shipped, correct end-to-end behavior — not framework completeness.

```text
Domain (facts + uses ids)
  → DomainSession (extensions loaded once: analyzers + maps)
  → session.Analyze (core pipeline + library INodeAnalyzer)
  → lower each action / policy / create / subscription → Syntax AST
  → session.Emit (entity C#) + bag-gated host files (DbContext / HTTP)
MCP harness: agent supplies context → simulate that same operation AST
```

**Hard lines**

| Truth | Implication |
|-------|-------------|
| Domain is a **module**, not a process | Do not invent `Main` / `Program.cs` in core. Capability/catalog is the operation menu |
| Shipped ⊆ lowerable | A construct is shipped only if it lowers to a complete, legal Syntax AST. Gaps stay in `docs/plans/`, not in the parser or guide |
| Always-legal **operations** | Each named operation is a full tree (no `Comment` / `null` / host walk as shipped meaning). Runtime and emit consume the same trees |
| AST is the symbolic primary | Do not reintroduce a parallel “primitive IR” for product paths |
| VM is canonical execution of a **program** (operation or algorithm) | `Poly.Interpretation` is a **generic language VM** for Syntax trees. DomainModeling is a client that lowers into that language. `Interpreter.Compile` fails closed on analysis errors. The LINQ expression path is a **same-tree semantic checker** (and inspectable execution) for the VM — not a second language. C# emit is a projection. Stored closures late-bind: analysis records free bindings per `Lambda`; emit shares a heap `long[1]` cell. A variable holding a lambda is a heap ref; `Invoke` of it takes the body kind. `Assignment` yields the RHS kind. Illegal `Invoke` targets fail at analysis. |
| Domain lowers to **generic** ops | No domain-specific VM opcodes. StageTransition is type-def + Assignment + `Invoke(Member(This, "Notify"))`. Self-invoke is `Invoke(Member(This, action))`. Cross-entity invoke is `this.Rel.Action(args)` with a `DomainResult.Failure` linked-target guard. For-invoke is a fail-fast `ForEachLoop` over a **OneToMany** collection nav (`if (!result.IsSuccess) return result`, zero-match `DomainResult.Failure`). Self/cross-entity lowering does not wrap `IsSuccess`. Remaining store/clocks (`Create` / create-in / time) still dual-path |
| Product doors are **opt-in extensions** | REST and the like load via `uses`. CLI flags seed ids only. Core seed does not emit a host |
| MCP is the **interactive harness** | Author, inspect, simulate by supplied context. Not the `DomainSession`. Not the customer API |
| Extend the platform **in the pipeline** | New meaning: lower to existing nodes, analyze, and/or **replace nodes** — not special-case the emitter, ABI, or one host’s type filter |
| Analysis is required for downstream semantics | Domain/runtime/tooling paths that resolve semantic meaning must consume an `AnalysisResult`; no semantic execution path without analysis |
| One coherent path | Prefer composing existing mechanisms over a parallel rewriter, evaluator, or type registry |
| Immutability at domain boundary | Mutate via `DomainEvolution`…`Apply`, not by editing graphs in place |
| Smallest coherent platform | Prefer using an existing mechanism over inventing a parallel one |

Policy: [`docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md`](decisions/2026-08-15-domain-library-extensions-mcp-harness.md).

TFM: `net10.0`, nullable on, zero external dependencies in core `Poly/`.

---

## 2. Separation of concerns

| Concern | Owns | Must not |
|---------|------|----------|
| **Ast** | `Node` records, `NodeId`, fluent construction API | Execution semantics, domain shapes, analysis logic |
| **Analysis** | Analysis framework (`Analyzer`, `AnalysisContext`, metadata store, **node replacement**) | Execution semantics, domain concepts, MCP session |
| **Interpretation** | Semantic passes, `Interpreter`, `DirectVmAbiEmitter`, VM runtime | Domain concepts; one-off ABI/emitter forks for a single consumer |
| **Introspection** | Platform-agnostic type/member model so Interpretation can **simulate any reasonable type system on any reasonable platform**; CLR is the first provider | Depending on Interpretation; baking one host into the core contract |
| **DomainModeling** | Immutable `Domain` (facts), evolution, `DomainExpression`, lower-to-AST **per operation**; **stage transitions are the authorable observable**; **`ImportedContract` is a used sub-domain** (owned value types + endpoints; `bind` is the only door — no `import` keyword; `internal` / `external` are producers of the same IR, including another Poly domain) | Domain VM opcodes; a process/`Main`; tree rewrites outside analysis + node replacement; merging child entities into the parent; growing `Comment` / `EffectExecutor` as shipped meaning |
| **Validation** | **Deleted 2026-08-09** (dead-dual cleanup — no product callers ever existed; see [`plans/dead-dual-inventory-2026-08-08.md`](plans/dead-dual-inventory-2026-08-08.md)); domain constraints live under DomainModeling constraints + domain analysis | Reintroducing a dormant rule surface; owning the AST or VM |
| **Grammar** | Pattern-table engine (`Poly/Grammar/`) — **language-shaped token streams**: tokenizer decodes (`IToken<TTokenKind>` + `BufferedTokenReader.ScanNextToken`); matcher recognizes (`Matcher`, longest-match **form tree**: `MatchResult.RuleName` / `Children` / `Operators`, `Tokens` is the span to consume); `Printer` + `ITokenWriter` emit; **`Language<TToken,TTokenKind>`** is the table + printer a session holds. `Grammar` is immutable; **`GrammarBuilder`** is the mutable construct path (`Commit` does not allocate a table; `Build` freezes once). `Extend` copies into a builder, applies contributions, freezes once. Duplicate `(rule, pattern)` fails closed. Nested Ref / Repeat / LeftAssoc use the same longest-match + `Priority` rule as top-level `TryMatch`. `NotFollowedBy` is zero-width negative lookahead on kind. `ListTokenReader` replays a decoded span (group interiors). Handlers own meaning (fold the tree; no product IR in Grammar); diagnostics positions are caller-owned | Product DSL table/handlers (owned by DomainModeling); do not grow parallel pattern engines (`Poly.Text.Matching` is historical — see dead-dual inventory) |
| **DomainModeling (DSL)** | One closed `.poly` language. `Domain` is facts (`uses` ids). `DomainSession` binds **concepts** those ids name (meaning, type maps, artifacts) — not new spell. Grammar is the product table; live parse is `MatchRule("expr-live")` + fold (with-not add/compare). Span `expr` / `expr-*-no-not` remain the GrammarMatch oracle (S1 `not`-in-chain still diverges). Folds map existing tokens (`Now`, `12 days`, `column(...)`) to IR. Never process-wide `Default`. | Extensible dialects; new token kinds per library; process-wide Temporal `*.Default`; `Domain.ResolveHost`; a second plugin host |
| **MCP (`Poly.Mcp`)** | Interactive **harness**: tool conversation, revision, scratch store; **simulate** named policies/actions when the caller supplies context | Domain mutation semantics; product entry points (that is an opt-in extension); a second evaluator; claiming capabilities the core does not have; inferring `Main` |
| **Synthesis** | Macros (VM validates) | Reverse deps from Interpretation |

**Enforced dependency direction (core):**

- `Interpretation` → `Syntax`, `Introspection`
- `DomainModeling` → `Syntax` for pure lowering (`DomainExpressionLoweringPass`). Execution model: [`docs/interpretation/domain-execution-model.md`](interpretation/domain-execution-model.md).
- Policy evaluation (domain-bound) bridges to Interpretation/VM via `DomainEntityInstance.EvaluatePolicy` → `DomainExpressionLoweringPass` → `Interpreter` — intentional consumption of the platform, not a license to fork the ABI. The CLR-subject wrapper `PolicyEvaluator` is **test-only** (`Poly.Tests/TestHelpers/`).
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
| Standard entry | `Interpreter.Analyze` / `Interpreter.Compile` (cached full pass list; **Compile fails closed** on `DiagnosticSeverity.Error`) |

**Principle:** Facts about a program live on nodes via analysis metadata. Do not attach parallel side tables or re-walk the tree outside the pass model for work that belongs in a pass.

**Contract:** For downstream consumers that answer semantic questions (lowering decisions, runtime semantic dispatch, MCP semantic inspection, compiler semantic mapping), analysis is non-optional. Those paths must fail closed when `AnalysisResult` or required metadata is missing.

**Domain analysis shape (shipped):** validate · catalog · derive; single catalog; export is not a domain-fact pass. Historical acceptance / cutover notes: [`plans/archive/domainmodeling-completed-2026-08/domain-analysis-future-state.md`](plans/archive/domainmodeling-completed-2026-08/domain-analysis-future-state.md), [`domain-analysis-simplification.md`](plans/archive/domainmodeling-completed-2026-08/domain-analysis-simplification.md).

**Bag + emit inventory:** [`plans/domainmodeling-metadata-artifact-catalog-2026-08-15.md`](plans/domainmodeling-metadata-artifact-catalog-2026-08-15.md) — who publishes each metadata bag, who emits files, and where library extension is real vs claimed.

**Domain catalog:** `DomainCatalogPass` is the first metadata pass (after structural well-formedness). It publishes `DomainCatalogMetadata` on the domain (types, relationships, actions, stages, owners) and aliases the same type/relationship maps on `default` for child-node walks. Later passes read that catalog; they do not rebuild name indexes. `TryGetStage` / `TryResolveAction` / `GetTypeLookup` go through it. Derived bags (capability, required-by-policy, dispatch plans, topology, storage, entity structure keys/ctor) stay later. **Relationships are entity-owned navigations:** `Domain.Relationships` is a computed flatten. `AnalyzeRequiringCatalog` requires the catalog for analyzable domains.

**Domain analysis door:** authoring, MCP, and compile analyze through `DomainSession.Analyze`. That pipeline constructs `StoragePass` with the session's type maps and storage conventions. Evolution and `McpSessionStore` must not call static `DomainModelAnalyzer.Analyze`. The static type remains as a test/runtime façade: `DomainModelAnalyzer.Analyze` forwards to `RuntimeAnalysisCache`, which opens a **core-catalog** session and calls `session.Analyze`. Vendor ids contribute no maps on that path.

**Subscription dispatch (stage + entity-level):** `RuntimeContractAnalyzer` publishes `SubscriptionDispatchPlanMetadata` on each **stage** (stage-scoped `when`) and each **entity** (always-active `Entity.Subscriptions`, empty plan when none). `DomainInstanceStore.NotifyTransition` requires catalog + relationship contracts; dispatches **stage plan first, then entity plan**; missing bags throw. The C# export consumes the SAME dispatch plan (no re-walk of `StageSubscription`): optional peer binder `when Rel Stage as name` — VM rewrites binder path-prefix against the transitioned peer; export emits quantifier-aware handlers `When{Any|All|Each}{Target}{Stage}(TargetType name)` and `sub.When…(this)`, one registry per (stage, subscriber) pair, notify fan-out to every handler. Analysis fail-closed for unbound peer-like roots, nested peer path-prefix, and peer assign targets. Historical suite: [`plans/archive/domainmodeling-completed-2026-08/simple-agent-tasks/spe-README.md`](plans/archive/domainmodeling-completed-2026-08/simple-agent-tasks/spe-README.md).

**Policy store reads:** `EvaluatePolicy` preprocesses Q3′ quantifiers, singular path-prefix (`RelationshipNavigation`), and relationship **`Rel exists` / `NotExists`** against outbound store links (`GetOutboundRelatedInstances`). Fail closed without store/domain for those forms; empty links → `exists` false (not throw).

**Effective stage surface:** `CapabilityAnalyzer` publishes the only effective view (`StageCapabilityMetadata` / `ActionCapabilityMetadata`). Downstream `GetEffectivePolicies` / `GetEffectiveActions` read that bag only — they do not recompose from the catalog. Name lookups with a domain key read the catalog only. `IsRoot` is `EntityStructureMetadata` (aggregate/storage copy it). Evolution still uses the catalog mutation index to apply changes.

**Fact vs validate packs (DAS W3.2):** Small fact emitters publish bags consumers read; megapass diagnostics stay in validate packs. Template split: `RequiredPropertiesPass` → `RequiredPropertiesMetadata`; `EffectFactsPass` → `ResolvedRelationshipTargetMetadata` on create-in. `PolicyConstraintAnalyzer` / `EffectAnalyzer` are lint-only (no fact publication). Historical split notes: [`plans/archive/domainmodeling-completed-2026-08/simple-agent-tasks/das-w3-2-split-validation-facts.md`](plans/archive/domainmodeling-completed-2026-08/simple-agent-tasks/das-w3-2-split-validation-facts.md).

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

Interpretation is the execution engine for Syntax programs (script = expression/`Block`; types = `TypeDefinitionNode` as analysis input). DomainModeling must not appear in the emitter/ABI. Dishonest passthroughs (`Await`, unresolved `ParameterReference`, `Comment` as `0`) are compile-reject or no-ops — not host escapes.

No intermediate primitive flattening step. Inputs are the AST plus analysis metadata (including replacements). **Principle:** keep the emitter a generic compiler of known nodes — fix upstream (lower / analyze / replace), do not patch the ABI for one scenario. Known-member `MethodInfo` / `PropertyInfo` / `ConstructorInfo`: `Ref` / `Ref<T>` (`Poly/Interpretation/Vm/Ref.cs`), never `typeof(T).GetMethod(...)`. Exception: `Expression<Func<T>>` cannot close over a ref struct, so `ReadOnlySpan<T>` constructors stay `GetConstructor`.

### 3.4 Domain expression lowering

| Piece | Location |
|-------|----------|
| Lower DE → Syntax AST | `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs` |
| Lower effects → Syntax AST | `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` — product path is a real node, not `null` |
| Module projection | `DomainProgramProjection.ToSyntax` — types + operations; **not** a compilation unit with `Main` |
| Policy compile/eval | `DomainEntityInstance.EvaluatePolicy` → `DomainExpressionLoweringPass` → `Interpreter` — **VM-primary**; LINQ dual-oracle + CLR-subject wrapper `PolicyEvaluator` are test-only (`Poly.Tests/TestHelpers/`) |
| Domain change | `DomainEvolution`…`Apply()` with analysis gate + rollback |

Domain concepts expand to **generic** Syntax nodes, not new opcodes. ADR: `docs/decisions/2026-06-08-domain-lowering-boundary.md`. **StageTransition** lowers to handwritten IR on both runtime and emit: Assignment of `CurrentStage` plus `Invoke(Member(This, "Notify"), stageName)` in `finally`. **Self-invoke** is the same shape: `Invoke(Member(This, action), args)` — analysis sees the action on the type def; C# prints `this.Checkout()`; runtime `This` has no Checkout CLR method so `InvokeNamed` runs the action (Notify still hits the real CLR method first). **Cross-entity invoke** is `this.Rel.Action(args)` with a linked-target `DomainResult.Failure` guard. **For-invoke** is a fail-fast `ForEachLoop` over a **OneToMany** collection nav (analysis rejects ManyToMany / OneToOne; per-item `InvokeNamed` returns `DomainResult`; `if (!result.IsSuccess) return result`; zero-match `DomainResult.Failure`; `ExecuteEffect` throws on a failed program result). Runtime collection navs on the type def / IDictionary are OneToMany **and** ManyToMany so member reads match lowering's collection predicate. Self / singular cross-entity do not wrap `IsSuccess` — nested Failure is discarded like C# `this.Foo();`. Remaining store effects (create / create-in) still dual-path via EffectExecutor. Sequential transitions in one action still share stale `SourceStageName` at lowering time. New work must not add consumer-specific lowering flags or a parallel effect interpreter. Residual dual-path is debt — do not grow it.

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

### 3.6 MCP (harness) and extensions (doors)

| Piece | Location |
|-------|----------|
| Tools / conversation | `Poly.Mcp/Tools/`, `Poly.Mcp/Sessions/` |
| Domain compile | `DomainSession` in `Poly/DomainModeling/` — MCP **holds** this; it is not the same type |
| Libraries | `IDomainLibrary` (`Id` + `Register`); product slot is `SessionBuilder.AddAnalyzer`. `uses sqlite` publishes persistence bag → DbContext; `uses http` publishes HTTP bag → Program.cs. Entity C# is `session.Emit`. `CompileMode` only seeds ids |

**MCP principle:** interactive harness for agents. Author, inspect, **simulate** a named operation when the caller **supplies context** (properties, stage, args, links, clock). Thin adapter — does not invent domain or execution semantics. Simulate runs the same lowered AST + `Interpreter` as emit. Scratch `DomainInstanceStore` is conversation state, not the production store. MCP is not a `uses` product host and not the customer API.

**Extensions:** `.poly` is one language. A `Domain` is facts (`uses` ids). A **`DomainSession`** loads those ids as analyzers (and type maps / folds they close over). It does not load a dialect. Spell is `DslGrammar.Core`. Another Poly domain is `ImportedContract`, not an extension id.

| Extension job | Loads when | Emits a process door? |
|---------------|------------|------------------------|
| Meaning (`temporal`) | `uses temporal` (SDK seed if source lists no `uses`) | no |
| Persistence (`storage`, `sqlite`, …) | listed / compiler seed | no |
| Product host (REST / HTTP, …) | **only** if listed | **yes** — binds already-lowered operations |

Unknown or duplicate ids fail closed. CLI `--dbms sqlite` seeds id `sqlite`; it does not imply HTTP. The compiler opens **one** session (`ForSource`/`ForExtensions` + extras) for parse, analyze, and artifacts. Core seed does not emit `Program.cs`. A host extension that cannot bind a lowered operation fails closed.

Folder `Libraries/` holds in-assembly seeds (Temporal, storage facets). Vendor packs stay in `src/`. The noun is **extension** / **library**.

### 3.7 Debugging / tracing (VM)

- Breakpoints: `VmState.DebugInterrupt` — `docs/decisions/2026-06-08-breakpoint-architecture.md`
- `Poly/Interpretation/Vm/`, `docs/interpretation/debugging-and-tracing.md`

---

## 4. Stop inventing this — use that

| Need | Use | Do **not** invent |
|------|-----|-------------------|
| Compile-time `MethodInfo` / `PropertyInfo` / `ConstructorInfo` for a known member | `Ref.Method` / `Ref.Constructor` / `Ref<T>.Method` / `Ref<T>.Property` / `Ref<T>.Indexer` (`Poly/Interpretation/Vm/Ref.cs`) | `typeof(T).GetMethod(...)` / `GetProperty` by string or `BindingFlags` |
| Rewrite or desugar AST | `SetNodeReplacement` / `INodeAnalyzer` | Product-local full-tree rewriter; emitter patches |
| Facts about a node | `IAnalysisMetadata` on `AnalysisContext` | Parallel side tables outside the metadata store |
| Resolve types / members | `ITypeDefinitionProvider` + `AnalysisContext.TypeDefinitions` | Ad-hoc reflection; second type registry; emitter method-lookup fallbacks |
| Stack host + custom types | `TypeDefinitionProviderCollection` | Hard-coding a single runtime into product modules |
| Run a program / policy / action | `Interpreter` on the **lowered operation AST** | Second evaluator; `Comment` as success; MCP-only semantics |
| Simulate in a conversation | MCP tool + **caller-supplied context** + same AST | Infer `Main`; treat MCP as the product API |
| Product entry point (REST, …) | Opt-in extension `uses` + `IArtifactContributor` | Core `Program.cs`; compiler flag that bypasses the catalog |
| Domain mutation | `DomainEvolution`…`Apply` | In-place graph edits; resurrecting V2 |
| Domain feature at runtime | Lower to existing Syntax ops (+ analyze/replace) | Domain opcodes; ABI special cases for one feature; grow `EffectExecutor` |
| Cross-cutting “why” | `docs/decisions/` | Re-litigating in drive-by comments |
| Multi-step work | `docs/plans/` | Expanding CORE or AGENTS into a plan |

---

## 5. Doc map (what to open next)

| Need | Open |
|------|------|
| Facet map + complexity demons | [`docs/complexity-semantic-map.md`](complexity-semantic-map.md) |
| Principles (values) | `AGENTS.md` + `docs/decisions/2026-core-engineering-principles.md` |
| Trust bar + first-customer strategy (T1–T3; product via domain + modules) | [`docs/decisions/2026-07-11-platform-trust-bar-and-dogfood.md`](decisions/2026-07-11-platform-trust-bar-and-dogfood.md) |
| Why of a major choice | `docs/decisions/README.md` |
| Domain = library; extensions = doors; MCP = harness | [`docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md`](decisions/2026-08-15-domain-library-extensions-mcp-harness.md) |
| Active execution work | `docs/plans/v2-to-v3/master-roadmap.md` (Agent pick) · `docs/plans/README.md` (admission) · `docs/plans/domainmodeling-workstream-map.md` |
| Module detail | `Poly/*/README.md`, `docs/interpretation/*` |
| Introspection detail | `Poly/Introspection/README.md`, `docs/technical/introspection.md` |
| Historical / may be stale | `docs/ARCHITECTURE.md` — prefer this file + module READMEs for truth |

---

## 6. Quick self-check before you ship

1. Did I compose an existing mechanism from §3 instead of a parallel one?  
2. Did I stay inside the ownership table in §2?  
3. If I needed a new shape or rewrite, did I lower / analyze / **replace nodes** rather than special-case the emitter, ABI, or a host type filter?  
4. If I added DSL/effect surface, does it lower to a complete operation AST (shipped ⊆ lowerable)? Did I avoid `Comment` / a second interpreter / a consumer-specific lowering flag?  
5. If I added a process door, is it an opt-in extension rather than core `Program.cs`?  
6. If I added MCP behavior, is it harness (author / inspect / simulate-with-context) on the same AST — not a private evaluator or inferred `Main`?  
7. Do docs (this file if mechanisms changed) still match the code?  
8. Build/tests green (`AGENTS.md` Build & Test).
