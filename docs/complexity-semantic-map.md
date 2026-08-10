# Poly — Semantic feature map & complexity demons

**Date:** 2026-08-08  
**Purpose:** Tour every major facet of the platform as a **semantic statement** (what it *is*, not which pattern it uses), then locate where **complexity demons** concentrate.  
**Audience:** Humans and agents prioritizing simplification vs feature work.  
**Authority for CURRENT work:** [`plans/simple-agent-tasks/PIPELINE-STATUS.md`](plans/simple-agent-tasks/PIPELINE-STATUS.md)  
**Authority for machinery:** [`CORE.md`](CORE.md)  

This is a **map**, not a suite. Update when a facet is deleted, merged, or materially changes meaning.

---

## 0. How to read this document

| Column / tag | Meaning |
|--------------|---------|
| **Semantic statement** | One sentence: the facet’s job in the product universe |
| **Live** | Product or test path exercises it today |
| **Dormant** | Code exists; no (or only commented) product callers |
| **Demon score** | 1–5: how much this facet *increases* cognitive load relative to its payoff (5 = worst) |
| **Dual of** | Another facet that answers a similar question |

**Complexity demon** = a second (or third) way to answer the same question, a layer that no longer has a consumer, or a name that hides two systems.

---

## 1. One-sentence platform

**Poly** is a neurosymbolic platform that turns **immutable domain models** (entities, stages, actions, policies, relationships) into **analyzable facts** and **executable behavior**, primarily by **lowering domain expressions to a generic symbolic AST**, then **compiling that AST to a VM**.

```text
Author (.poly / builders / MCP / evolution API)
  → Domain (immutable) + DomainExpression + Effect
  → DomainEvolution.Apply  [domain analysis gate]
  → lower DE → Ast
  → program analysis + node replacement
  → DirectVmAbiEmitter → VM
```

Everything else is either a **facet of that spine** or a **satellite** (export, packs, dead duals, docs process).

---

## 2. Size snapshot (approx., 2026-08-08)

| Area | ~LOC (`.cs`) | Role |
|------|-------------:|------|
| DomainModeling | 19 200 | Product ontology + DSL + domain analysis + runtime |
| Interpretation (+ VM emitter) | 12 000 | Program analysis + VM + backends |
| Poly.Tests | 41 000 | Oracle wall |
| Text | 3 000 | Historical text stack (mostly unused by product) |
| MCP | 2 900 | Session tools |
| Introspection | 2 000 | Host-neutral types |
| Ast | 2 100 | Symbolic IR nodes |
| DslCompiler + packs | 2 100 | Codegen host |
| Analysis framework | 1 200 | Pass substrate |
| Grammar | 1 100 | Pattern-table engine |
| Validation | 500 | **Dormant** |
| Docs (markdown) | ~535 files | Process + history (plans alone ~455) |

**Largest single files (heat):** `DirectVmAbiEmitter` ~3k · `DomainToCSharpExporter` ~1.5k · `DomainEntityInstance` ~1.4k.

---

## 3. Semantic layers (every major facet)

### 3.1 Symbolic program substrate

| Facet | Semantic statement | Live | Demon |
|-------|-------------------|------|------:|
| **Ast / Node** | *A program is an immutable tree of typed nodes with identity (`NodeId`).* | Yes | 1 |
| **Ast node catalog** (~70 expr/stmt + ~18 type-definition nodes) | *The platform can represent general-purpose program shapes (ops, control flow, types), not only domain policies.* | Yes | 2 — broad IR vs thin product use |
| **Analysis framework** | *Facts and rewrites about a program attach to nodes via passes and metadata, without mutating the tree.* | Yes | 1 |
| **Node replacement** | *Desugar/simplify is registered as original→replacement metadata; backends honor replacement.* | Yes | 1 |
| **Program analysis passes** (types, members, CFG, side effects, folding, jumps, EH regions, …) | *Before execution, the program is classified for correctness and compilation.* | Yes | 2 — many passes; most domain path only needs a subset |
| **Interpreter façade** | *Analyze / Compile / Execute is one entry surface for programs.* | Yes | 1 |
| **DirectVmAbiEmitter** | *Ast (+ analysis) becomes a VM program without a primitive IR middle layer.* | Yes | 3 — size/complexity of one file |
| **VmState / VmProgram** | *Canonical execution is a stateful VM (stack, heap, suspend/debug hooks).* | Yes | 2 |
| **LinqExpressionGenerator** | *A second backend compiles Ast to CLR delegates for oracle/parity.* | Yes (tests + Validation) | **4 — dual evaluator** |
| **CSharpGenerator** | *Ast (and domain export) can be printed as C# source.* | Yes (export / DslCompiler) | 1 |
| **MermaidAstGenerator** | *Ast can be visualized as Mermaid.* | Tests / tooling | 1 |
| **Introspection** | *Type/member identity is host-neutral; CLR is the first provider.* | Yes | 2 — multi-host ambition vs single host |
| **CLR type registry** | *Reflection-backed types feed resolution for VM/compile.* | Yes | 1 |

### 3.2 Domain ontology (what the product models)

| Facet | Semantic statement | Live | Demon |
|-------|-------------------|------|------:|
| **Domain** | *A named, immutable collection of types and relationships.* | Yes | 1 |
| **Entity** | *A lifecycle-bearing type: properties, stages, actions, policies, subscriptions.* | Yes | 1 |
| **Stage** | *A named lifecycle state with entry/exit effects and stage-local actions/policies.* | Yes | 1 |
| **Action** | *A named operation with parameters, guards, effects, optional return type.* | Yes | 1 |
| **Policy** | *A named guard expression (DomainExpression) scoped to entity/stage/action.* | Yes | 1 |
| **Relationship** | *A first-class link between entity types (nav properties / cardinality).* | Yes | 1 |
| **Property + constraints** | *Attributes with required/unique/range/length/pattern/… rules.* | Yes | 1 |
| **Enum / Primitive / Value types** | *Non-entity type vocabulary in the domain graph.* | Yes | 1 |
| **Facet / Annotation** | *Extensible metadata on definitions (e.g. SQL column/table packs).* | Yes | 2 — pack surface |
| **ImportedContract / endpoints** | *External contract binding vocabulary for integration modeling.* | Partial | 3 — advanced / low dogfood |
| **StageSubscription** | *When a related entity reaches a stage, run effects (observable = stage transition).* | Yes | 2 |
| **Effect family** | *Authorable mutations: assign, transition, create, create-in, delete, invoke, conditional, composite, link/unlink, …* | Yes | 2 — surface breadth |
| **DomainExpression family** | *Authorable predicates/values: literals, props, arith, logic, compare, path-prefix, exists, quantifiers, date ops, …* | Yes | 2 |
| **DomainObject / DomainMember** | *Shared base for domain graph participation and analysis attachment.* | Yes | 1 |

### 3.3 Domain mutation & evolution

| Facet | Semantic statement | Live | Demon |
|-------|-------------------|------|------:|
| **DomainChange catalog** (~40+ change records) | *Every structural edit is an explicit immutable change type.* | Yes | **3 — combinatorial API** |
| **DomainEvolution / Apply** | *Changes apply transactionally through domain analysis; failure rolls back.* | Yes | 1 |
| **EvolutionTrace / Transaction** | *Operators can inspect what happened / batch mutations.* | Yes | 1 |
| **Builders** (Domain/Entity/Stage/Action/…) | *Fluent code constructs the same domain graph as DSL.* | Yes (tests) | **3 — second authoring surface** |
| **Bootstrap / DomainFactory** | *New domains start with canonical built-in primitives.* | Yes | 1 |

### 3.4 Domain analysis (facts about the *domain graph*)

**Semantic statement:** *Before a domain is trusted for runtime or export, analyzers publish structured facts and diagnostics about the model.*

| Facet | Semantic statement | Live | Demon |
|-------|-------------------|------|------:|
| **DomainModelAnalyzer** | *Orchestrates domain analysis pipeline.* | Yes | 2 |
| **Catalog** (`DomainCatalogPass` + metadata) | *Single product name→member map for lookups.* | Yes | 2 — intermediate bags still exist |
| **Structural / Semantic domain analyzers** | *Well-formedness and cross-ref of domain structure.* | Yes | 2 |
| **Capability / Effective surface** | *What policies/actions apply at a stage is one composition algorithm.* | Yes | 2 |
| **Effect topology / Effect facts** | *Effect targets and graph of effect structure for lint and codegen.* | Yes | 2 |
| **Runtime contracts / subscription plans** | *Dispatch plans for stage- and entity-level `when`.* | Yes | 2 |
| **Ownership aggregates** | *Owned-graph structure for aggregates.* | Yes | 2 |
| **Storage / Transport passes** | *Infrastructure projections (DB columns, API surface models).* | Packs / export | **3 — infra vs product ontology** |
| **PolicyConstraint / Effect / RuleCoverage / AuthoringSuggestion** | *Lint and advisory diagnostics (not always fact publishers).* | Yes | 2 |
| **RuntimeAnalysisCache** | *Domain analysis results cached for runtime.* | Yes | 1 |
| **PassRegistry** | *Packs inject extra analyzers after built-ins.* | Packs | 2 |
| **~20 metadata bag types** | *Typed facts on domain nodes for consumers.* | Yes | **4 — bag sprawl / dual lookups** |

**Demon note:** “Analysis” means **two systems** (domain graph vs program AST). Same English word; different objects. Always say **domain analysis** vs **program analysis**.

### 3.5 Domain runtime

| Facet | Semantic statement | Live | Demon |
|-------|-------------------|------|------:|
| **DomainEntityInstance** | *A live instance: property bag, stage, action invoke, policy eval, effect execution.* | Yes | **4 — god object (~1.4k LOC)** |
| **DomainInstanceStore** | *Registry of instances + relationship links + transition notify.* | Yes | 2 |
| **Policy evaluation on instance** | *Guards run via lower→VM after quantifier/path preprocess against the store.* | Yes | **3 — preprocess + VM dual steps** |
| **Effect execution split** | *Some effects lower to VM; others mutate instance/store directly.* | Yes | **4 — two execution strategies** |
| **DomainExpression rewrite (runtime)** | *Before lower, rewrite DE for store-aware forms (quantifiers, peer binders, …).* | Yes | 3 |
| **InvocationResult** | *Structured outcome of actions/effects.* | Yes | 1 |

### 3.6 Language: Grammar engine + product DSL

| Facet | Semantic statement | Live | Demon |
|-------|-------------------|------|------:|
| **Grammar Matcher** | *At each position, longest-match among patterns in a named rule.* | Yes | 1 |
| **Pattern elements** (Token, Value, Predicate, Optional, Many, Rule, LeftAssoc, Balanced, Any) | *Composable recognition units; IR fold is not the engine’s job.* | Yes | 2 |
| **Grammar Printer / TokenWriter** | *Walk pattern tables to emit text (kind-centric).* | Partial (tests / non-product) | 2 |
| **Engine Token&lt;TKind&gt;** | *Engine-owned kind+text+line/col+payload (text-biased).* | Yes | **4 — design lie; see grammar-revision** |
| **DslTokenReader / DslTokenKind** (~60+ kinds) | *`.poly` text becomes a kind stream.* | Yes | 1 |
| **DslGrammar tables** | *Product structural + expr span + effect heads + ops as patterns.* | Yes | **3 — span tables vs live fold** |
| **DslExpressionParser Option A ladder** | *Live expr IR fold is a precedence ladder guided by MatchRule ops.* | Yes | **4 — pure claim vs ladder reality** |
| **PolyDslParser structure + effect handlers** | *Top/entity/stage/action/effect dispatch → DomainChange / Effect IR.* | Yes | 2 |
| **DomainDslPrinter** | *Domain graph walks back to `.poly` text (not Grammar printer).* | Yes | **3 — dual printer path** |
| **ExpressionFormRegistry (E1)** | *Packs inject primary expression forms without core parser edits.* | Yes (tests/packs) | 2 |
| **AnnotationRegistry** | *Pack keywords for annotations with handler validation.* | Yes | 2 |
| **DslExpressionFragment** | *Parse a single expression string (MCP policies) fail-closed.* | Yes | 1 |
| **poly-dsl-guide** | *Product-true syntax contract for agents and humans.* | Yes | 1 if kept in sync; **3 if drift** |
| **grammar-revision (design)** | *Future: language-owned tokens; caller-supplied exception positions.* | Design lock | — |

### 3.7 Authoring surfaces (how domains enter the system)

| Facet | Semantic statement | Live | Demon |
|-------|-------------------|------|------:|
| **`.poly` + apply_dsl** | *Canonical bulk authoring medium for the product.* | Yes | 1 (intended center) |
| **MCP add / remove** | *Incremental structure via kind+JSON payload; expressions as DSL strings.* | Yes | 3 — payload schema is the last shape-typed JSON surface (B1 `pattern`/`regex` class); **retirement designed in [`plans/dsl-delta-fragments.md`](plans/dsl-delta-fragments.md)** |
| **MCP session** | *Stateful workspace: domain revision + optional instances.* | Yes | 1 |
| **Fluent builders** | *C# API builds the same graph.* | Tests / demos | **3** |
| **Direct DomainEvolution in tests** | *Tests apply DomainChange lists without DSL.* | Tests | 2 |
| **DslCompiler CLI** | *`.poly` → C# / DbContext / Minimal API / .http via packs.* | Host | 2 |

**Four authoring surfaces** → one Domain is correct only if round-trip and analysis keep them honest. **Demon: multi-surface without one round-trip bar.**

### 3.8 MCP tool catalog (~24 tools)

| Cluster | Semantic statement | Tools (examples) |
|---------|-------------------|------------------|
| **Session** | *Create and list domain sessions.* | `create_domain_session`, `list_sessions` |
| **Inspect** | *Read model structure and analysis.* | `get_domain_overview`, `get_entity_detail`, `get_relationships`, `get_constraints`, `get_domain_analysis`, `get_domain_suggestions`, `describe_domain_element`, `get_policy_expression` |
| **Mutate structure** | *Incremental evolve via unified kinds.* | `add`, `remove` |
| **Mutate bulk language** | *Replace domain from `.poly`.* | `apply_dsl`, `export_dsl`, `get_dsl_guide` |
| **Policy oracle** | *Evaluate/simulate guards via VM path.* | `evaluate_policy`, `simulate_policy` |
| **Runtime instances** | *Create/link/invoke instances in session store.* | `create_instance`, `link_instances`, `unlink_instances`, `get_instance`, `list_instances`, `invoke_action` |
| **Export** | *Domain → C# text.* | `export_domain_to_csharp` |

**Demon:** tool *honesty* vs core capability (AGENTS/trust bar) — MCP must not claim what domain/runtime cannot do.

### 3.9 Export & infrastructure packs

| Facet | Semantic statement | Live | Demon |
|-------|-------------------|------|------:|
| **DomainToCSharpExporter** | *Domain model → C# type/method surface.* | Yes | 2 |
| **DomainProgramProjection** | *Domain → program Syntax IR for export paths.* | Yes | 2 |
| **EffectLoweringPass** | *Effects → Ast statements for compile/export.* | Yes | 2 |
| **Storage conventions / emitters** | *Map domain properties to store columns.* | Packs | 2 |
| **SQLite / SQL Server / MySQL packs** | *Host-specific defaults (annotations, identifiers).* | Host | 2 |
| **DbContext / MinimalApi / HttpFile generators** | *Productize domain as app skeleton.* | DslCompiler | 2 |
| **demo/RestApi** | *Sample host application.* | Demo | 1 |

### 3.10 Dormant / historical duals

| Facet | Semantic statement | Live | Demon |
|-------|-------------------|------|------:|
| **Poly.Validation Rule/RuleSet** | *Compose rules → Ast → LINQ predicate.* | **No product callers**; tests commented | **5 — dead dual of domain constraints + policies** |
| **Poly.Text.Matching** | *String pattern mini-language (linked automaton).* | Benchmarks only | **5 — dead dual of Grammar** |
| **Poly.Text StringView/Parsers** | *Low-level text utilities.* | Not used by product | **3 — unused substrate bulk** |
| **Primitive IR / ToPrimitives** | *Former canonical middle IR.* | **Deleted** | 0 (historical ADR only) |
| **Tree-walker interpreter** | *Former “canonical” evaluator.* | **Deleted** | 0 |
| **DomainExpressionJsonParser** | *JSON IR for expressions on MCP.* | **Deleted** (mcp-minify) | 0 |
| **V2 Data.Modeling** | *Previous domain stack.* | **Deleted** | 0; residual **V3 naming** = demon 2 |

### 3.11 Process & documentation facets

| Facet | Semantic statement | Demon |
|-------|-------------------|------:|
| **CORE.md** | *Always-on machinery map.* | 1 if short; grows → 3 |
| **AGENTS.md** | *Principles + placement + build.* | 1 |
| **poly-dsl-guide** | *Product syntax truth.* | 2 if dual with experiments |
| **ADRs (decisions/)** | *Why historical choices.* | 1 |
| **Plans / suites / gates / follow-ups** | *Agent execution queues.* | **4 — paper trail inflation** |
| **Archive plans (~300+ md)** | *Completed work history.* | **3 — search pollution** |
| **Experiments / roadblocks / PROJECT-SUMMARY** | *Lab and onboarding prose.* | **3 — false authority risk** |
| **PIPELINE-STATUS monopath** | *Single CURRENT truth.* | 1 (anti-demon) |
| **phenomenal-review / pr1** | *Adversarial quality loops.* | 2 — high value, high noise if stacked |

---

## 4. End-to-end paths (what “works” means)

| Path | Steps | Primary demons |
|------|-------|----------------|
| **Author → model** | `.poly` / add / builders → DomainEvolution → domain analysis | Multi-surface, DomainChange breadth |
| **Policy evaluate** | DE → (runtime rewrite) → lower → program analysis → VM | Rewrite + VM; LINQ dual in tests |
| **Action invoke** | Guards → effects (VM *or* direct) → stage notify → subscriptions | Dual effect execution; instance god-object |
| **Export app** | Domain analysis facts → generators + packs → C# | Storage/transport bag coupling |
| **MCP agent loop** | tools ↔ session ↔ same core paths | Catalog honesty; status doc drift |

---

## 5. Complexity demons (ranked)

### Tier S — structural duals (fix or accept explicitly)

| # | Demon | What two things answer | Recommendation |
|---|--------|------------------------|----------------|
| **D1** | **DomainExpression vs Ast** | “What is an expression?” | **Accept** (domain IR vs program IR). Cost: every new form twice. Mitigate with one lowerer + parity. |
| **D2** | **Domain analysis vs program analysis** | “What is analysis?” | **Accept** layers; **rename in speech** always. |
| **D3** | **VM vs LINQ** | “What runs a program?” | VM primary; **keep LINQ only as oracle** until retired. |
| **D4** | **Expr span tables vs Option A ladder** | “What is the expression language?” | **grammar wrap-up** (LeftAssoc live-fold) or reword pure claims forever. |
| **D5** | **Domain-walk printer vs Grammar printer** | “How do we print?” | Defer table print *or* commit; don’t claim pure print. |
| **D6** | **Effect: VM-lowered vs direct-exec** | “How does an effect run?” | Document matrix; long-term one strategy where possible. |
| **D7** | **Four authoring surfaces** | “How do I create a domain?” | **DSL-canonical**; others must round-trip. **Consolidation path designed:** [`plans/dsl-delta-fragments.md`](plans/dsl-delta-fragments.md) (fragment submissions + `remove` keyword retire MCP `add`/`remove` → back to a single authoring language) |

### Tier A — dead or dormant weight (delete candidates)

| # | Demon | Evidence | Recommendation |
|---|--------|----------|----------------|
| **D8** | **Validation module** | ~500 LOC; zero product callers; tests commented | **Delete** (dead-dual inventory) |
| **D9** | **Text.Matching** | ~unused; dual of Grammar | **Delete** (not rebuild in grammar-revision tier A) |
| **D10** | **Text bulk** | ~3k LOC mostly unreferenced by product | Inventory StringView/Parsers; delete or extract |
| **D11** | **Archive/plan search fog** | ~455 plan md files | Front-door + ignore archive in agent search |

### Tier B — concentration of complexity (live but heavy)

| # | Demon | Symptom | Recommendation |
|---|--------|---------|----------------|
| **D12** | **DomainEntityInstance god-object** | ~1.4k LOC; policy + effects + rewrite | Split effect runner / policy preprocessor |
| **D13** | **DirectVmAbiEmitter monolith** | ~3k LOC | Only touch with program needs; no domain forks |
| **D14** | **DomainChange combinatorial surface** | 40+ change types | MCP unifies add/remove; don’t grow micro-changes casually |
| **D15** | **Metadata bag sprawl** | ~20 domain metadata types | Catalog-first; refuse new bags without consumer |
| **D16** | **Runtime DE rewrite + lower + VM** | Three stages for one policy | Document; simplify only with store-aware lower later |
| **D17** | **Engine Token text bias** | Line/Col/Text on engine token | **grammar-revision** tier A when admitted |
| **D18** | **Infra passes in domain analysis** | Storage/Transport next to ontology | Clear “product vs pack” boundary in CORE speech |
| **D19** | **Process suite inflation** | README+tasks+gate+review+follow-ups per stream | Status monopath; archive DONE suites aggressively |
| **D20** | **V3 / Phase 1a naming** | History in type names and docs | Naming cleanup when idle |

### Tier C — acceptable complexity (payoff ≥ cost)

| Facet | Why keep complexity |
|-------|---------------------|
| Immutable Domain + evolution gate | Correctness / rollback |
| Fail-closed analysis | Trust bar |
| Catalog + subscription plans | Real product routing |
| Grammar longest-match + RuleRef/LeftAssoc | Real language machinery |
| Packs (annotation + expression forms) | Extension without core forks |
| Dual-oracle tests (VM/LINQ) | Safety net while LINQ lives |

---

## 6. Heatmap (where demons cluster)

```text
                    High dual / dead weight
                              ▲
                    Validation · Text.Matching
                    Plan archive fog
                              │
         Expr ladder↔span     │     DE↔Ast (accepted)
         Printer dual         │     Domain vs program analysis
         Effect VM↔direct     │
                              │
    ──────────────────────────┼──────────────────────────► Live product value
                              │
         DomainEntityInstance │     Evolution + catalog
         Emitter size         │     .poly + MCP add/remove
         Metadata bags        │     VM policy path
                              │
                              ▼
                    High concentration, still live
```

**Sweet spot to attack first (high demon, low product risk):** D8, D9, D11.  
**Sweet spot for product honesty:** D4, D5, D7.  
**Do not “simplify” without a design:** D1, D2, D3.

---

## 7. Facet checklist (quick inventory)

Use this as a kill/keep board:

| Facet | Keep | Merge | Delete | Document-only |
|-------|:----:|:-----:|:------:|:-------------:|
| Ast + program analysis + VM | ✓ | | | |
| Domain ontology + evolution | ✓ | | | |
| Domain analysis catalog/capability/subscriptions | ✓ | bags↓ | | |
| DomainExpression (domain IR) | ✓ | | | |
| LINQ backend | oracle | | later | |
| Validation | | | ✓ | |
| Text.Matching | | | ✓ | |
| Text StringView/Parsers | ? | extract | ? | |
| Grammar engine | ✓ | revision | | |
| Option A ladder vs LeftAssoc span | wrap-up | | | |
| DomainDslPrinter | ✓ | | | until table print |
| Builders | tests | | | |
| MCP unified tools | ✓ | | | |
| Storage/Transport | packs | | | |
| grammar-revision design | | | | ✓ until suite |
| Suite paper trail | thin | | archive | |

---

## 8. Recommended use

1. **Before starting a feature:** find its facet row — if Demon ≥ 4, state how you avoid a new dual.  
2. **Before admitting a suite:** map it to D1–D20; prefer closing a demon over opening a parallel one.  
3. **Simplification order (suggested):**  
   - Status monopath (done)  
   - Delete Validation + Text.Matching (dead-dual)  
   - **grammar-revision tier A (D17)** — **before** wrap-up: the LeftAssoc fold (D4) touches the same engine files the migration rewrites; revision-first avoids folding twice (see [`plans/grammar-revision.md`](plans/grammar-revision.md) §Status P1)  
   - Grammar wrap-up (D4) on the revised stack  
   - Instance/effect split only when invoke path hurts  

4. **Do not** start a “rewrite Poly” mega-suite. Demons die one dual at a time.

---

## 9. Related docs

| Doc | Role |
|-----|------|
| [`CORE.md`](CORE.md) | Machinery you must not reinvent |
| [`plans/dead-dual-inventory-2026-08-08.md`](plans/dead-dual-inventory-2026-08-08.md) | Validation/Text kill evidence |
| [`plans/grammar-revision.md`](plans/grammar-revision.md) | Token/exception re-vision lock (D17) |
| [`plans/dsl-delta-fragments.md`](plans/dsl-delta-fragments.md) | Fragment authoring design (D7 consolidation — MCP `add`/`remove` retirement) |
| [`plans/grammar-pure-end-state.md`](plans/grammar-pure-end-state.md) | Pure Grammar product direction |
| [`plans/simple-agent-tasks/PIPELINE-STATUS.md`](plans/simple-agent-tasks/PIPELINE-STATUS.md) | CURRENT admit |
| [`Poly.Mcp/Docs/poly-dsl-guide.md`](../Poly.Mcp/Docs/poly-dsl-guide.md) | Product DSL surface |
| [`agent/reviews/2026-08-08-long-term-growth-review.md`](agent/reviews/2026-08-08-long-term-growth-review.md) | Full-project growth review (demons → roadmap) |

---

## 10. Maintenance

When you **delete, merge, or re-home** a facet: update §3 row, §5 demon list, and §7 checklist in the **same change**. Stale complexity maps become complexity demons.
