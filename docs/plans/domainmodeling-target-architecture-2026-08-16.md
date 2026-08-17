# DomainModeling — target architecture & layout

**Date:** 2026-08-16 (edited 2026-08-17: F1–F11; r2 re-verify items 1–6)
**Status:** Proposal (target shape for review). **Not CURRENT.** Not a migration plan — do not implement from this document. Not admitted via `simple-agent-tasks/PIPELINE-STATUS.md`.
**Lock (do not reopen):** [`docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md`](../decisions/2026-08-15-domain-library-extensions-mcp-harness.md) · [`2026-08-14-domain-libraries.md`](../decisions/2026-08-14-domain-libraries.md) · `docs/CORE.md` · AGENTS platform facts.
**Complements (does not replace):** [`domainmodeling-vision-cleanup-2026-08-16.md`](domainmodeling-vision-cleanup-2026-08-16.md) — the executable deletion of dual paths. This document is the **end-state layout** those deletions converge toward.
**Review that forced this edit:** [`docs/agent/reviews/2026-08-17-target-architecture-review.md`](../agent/reviews/2026-08-17-target-architecture-review.md).

---

## 0. Organizing principle (one sentence)

**The phases of the pipeline are the architecture: organize by data-flow, not by type family or GoF pattern.**

DomainModeling is a **front-end compiler**: DSL → facts → analysis → AST. It hands the AST to Interpretation and never runs a second evaluator. It **does emit `.poly`** (the product surface); it does **not** emit host text (C#/SQL/HTTP) — that is Interpretation (`CSharpGenerator`) or an opt-in host extension. Anything in the tree that emits host text or runs a second evaluator is an accreted layer, not a missing piece.

---

## 1. The pipeline

```text
Author (.poly / MCP)
  → facts (Domain)
  → compile (DomainSession: load libraries, freeze tables)
  → analyze (fail-closed → catalog + derived bags)
  → print (.poly via Language/)                      ← DomainModeling, product
  → lower (one complete legal Syntax AST per operation)
  → Interpretation (VM for runtime, CSharpGenerator for host emit)   ← outside DomainModeling
```

Each phase has **one input and one output**; the session threads the frozen tables (language, folds, meaning, passes, artifacts) through them. Analyze stamps metadata bags; evolution is analysis-gated mutation — these are not "pure transforms," and the pipeline should not be read as forbidding side tables.

---

## 2. Target layout

> **Target names only** — current paths are in §4. **No folder moves until cleanup slices 1–2 land** (session exists without `DomainHost`). This file does not admit a rename CURRENT.

```
Poly.DomainModeling/
  Ontology/          # records + derived flatten (Domain.Relationships) — facts, no walkers
    Domain, Entity, Property, Stage, Action, Policy, Relationship,
    EnumType, PrimitiveType, ValueType, DomainType, DomainTypeReference, DomainExpression,
    Effect, Constraint, Facet, Annotation, AnnotationValue, DomainMember, DomainObject,
    StageSubscription, SubscriptionEventAccess
    Constraints/   Effects/   Contract/            # ImportedContract, endpoints, bindings
    Bootstrap/                                     # DomainFactory, built-in primitives (facts factory)

  Dispatch/          # closed-world walkers over the domain IR (NOT facts)
    DomainExpressionDispatch<T>, EffectDispatch<T>

  Compile/           # the compile unit — "session is the compile"
    DomainSession        # parse · analyze · print(.poly) · lower · contribute artifacts — the ONE coordinator
    SessionBuilder       # mutable assembly; IDomainLibrary.Register(this)
    ExtensionCatalog     # id → library
    IDomainLibrary
    IArtifactContributor # the "Emit" slot on the session
    DomainCompilation    # PeekExtensions / WithSeed (compile helpers, not a library)

  Language/          # the closed .poly spell — parse AND print
    DslGrammar, DslToken, DslTokenKind, DslTokenReader, DslTokenWriter,
    PolyDslParser, DomainDslPrinter, DslExpressionParser, DslCursor, DslExpressionFragment

  Meaning/           # concept bindings on existing spell (not new productions)
    ExpressionFoldTable, ExpressionFormRegistry, ExpressionPrintMapping,
    CoreExpressionPrintBinders, ExpressionDispatchRegistry,
    ExpressionDefaultResolverRegistry,
    ExpressionMeaning, AnnotationRegistry, IAnnotationSyntax, SqlAnnotationSyntax

  Analysis/          # one question → one bag; catalog is first
    Catalog/  Capability/  Structure/  Subscriptions/  Storage/
    + type-check config (ExpressionTypeCheckRegistry — stays beside ExpressionTypeAnalyzer)
    + diagnostic/hint passes (Effect, PolicyConstraint, ExpressionType, RuleCoverage, …)
    + orchestrator = session.Analyze (not a static cached class)

  Lowering/          # the semantic contract: facts → complete Syntax AST
    DomainExpressionLoweringPass, EffectLoweringPass, LoweringContext,
    DomainProgramProjection          # domain → language-agnostic program AST

  Runtime/           # harness: bag + stage + store + "run this program"
    DomainEntityInstance (thin), DomainInstanceStore, InvocationResult
    # DomainExpressionRewriteBase → delete (use analysis SetNodeReplacement) or move to Lowering/

  Evolution/         # immutable mutation
    DomainChange, DomainEvolution, DomainMutationContext, EvolutionResult, EvolutionTrace

  Queries/           # MCP projections (DomainQueries) — stays; not a phase
  Libraries/         # in-assembly seed extensions (renamed from Packs/)
    Temporal/   Storage/
    # vendors (sqlite/sqlserver/mysql) and http stay in src/ assemblies — NOT here (see O3)
  ContractFill/      # another Domain → ImportedContract (NOT session load)
    InternalDomainProducer, DomainSuite
```

---

## 3. Structural moves

**Locked (pipeline + three nouns — consequences of the ADR/CORE):**

- **M2 — The session owns the phases.** `Analyze`, `Print`, and `Emit` are operations on the closed world, not a static `DomainModelAnalyzer` and a CLI `GenerateAllFiles` that ignore what was loaded. `Emit` here is the **target** (`session.Emit` once contributors replace the thin loop): cleanup slices 1–2 require only `Analyze` (and parse/print) on the session — `GenerateAllFiles` may remain a reader until contributors land.
- **M5 — Analysis is single-purpose.** One catalog (the name index), then derived bags that each answer one question. The pipeline is registered by the core seed at `Open`, so a library's `AddPass` actually runs.
- **M6 — Libraries are the only place meaning can differ.** `Meaning/` is *configuration of the closed language*, seeded by `uses`. Contract fill (`InternalDomainProducer`, `DomainSuite`) is **not** session load.

**Target after host-ABI / emit-on-session (NOT locked — same "still a lie" as cleanup slice 3):**

- **M1 — Finish the projection; move C# idiom out of core.** `DomainToCSharpExporter` already returns Syntax (`Export` = `DomainProgramProjection.ToSyntax`), but `ToSyntax` still calls back into the exporter's static builders (subscriptions, value types, `DomainResult`, contract adapters) — a **split walk**, not a string printer. The move is: finish `ToSyntax` as the single domain→AST walk (fold the exporter builders into it), and move C# idiom nodes (`DomainResult<T>`, `Create` factories) into a named C# target contributor (home = O2). This closes the remaining emit-side dual; it is **not** "stop printf."
- **M3 — Lowering has one shape.** `Lowering/` holds the DE pass, the effect pass, and the `ToSyntax` projection. Every shipped operation lowers to **generic** nodes; store/clocks lower to host-ABI external calls. `ExecuteStructured` + the emit-mode `LoweringContext` (`LowerStageTransitions` etc.) die **only when** that host-ABI lowering lands — the cleanup plan's slice 3 keeps the seam and says one-lowering is still a lie. Do **not** read this layout as license to delete the runtime seam early.
- **M4 — Runtime shrinks to a harness.** `DomainEntityInstance` shrinks to *bag + current stage + store reference + hand the program to Interpretation*. Effect execution and policy evaluation become "lower → AST → `Interpreter`"; create/link/notify become host-ABI calls. This is host-ABI work, not a folder move.

---

## 4. Current → target map (non-obvious re-homes)

| Today lives at | Target home | Move |
|----------------|-------------|------|
| `DomainModeling/DomainHost.cs` (`DomainHost`, `DomainHostBuilder`, `DomainParserInputs`, `DomainAnalysisInputs`) | `Compile/` (folded into `DomainSession` + `SessionBuilder`) | delete nouns |
| `DomainModeling/Packs/ExtensionCatalog.cs`, `IDomainLibrary.cs`, `IArtifactContributor.cs`, `DomainCompilation.cs` | `Compile/` | extension *mechanism* + compile helpers |
| `DomainModeling/Packs/Temporal/` | `Libraries/Temporal/` | in-assembly extension |
| `DomainModeling/Packs/StorageFacetLibrary.cs` | `Libraries/Storage/` | in-assembly extension |
| `DomainModeling/Packs/InternalDomainProducer.cs`, `DomainSuite.cs` | `ContractFill/` | **not** a library |
| `DomainModeling/Parsing/*` | `Language/` (parse **and** print) | move |
| `DomainModeling/ExpressionFormRegistry.cs`, `ExpressionFoldTable.cs`, `ExpressionPrintMapping.cs`, `ExpressionDispatchRegistry.cs`, `ExpressionDefaultResolverRegistry.cs`, `ExpressionMeaning.cs`, `AnnotationRegistry.cs`, `Parsing/CoreExpressionPrintBinders.cs` | `Meaning/` | move |
| `DomainModeling/Analysis/ExpressionTypeCheckRegistry.cs` | `Analysis/` (stays — type-check config beside `ExpressionTypeAnalyzer`) | no move |
| `DomainModeling/IAnnotationSyntax.cs`, `SqlAnnotationSyntax.cs` | `Meaning/` | move |
| `DomainModeling/DomainExpressionDispatch.cs`, `EffectDispatch.cs` | `Dispatch/` (top-level walkers, not Ontology) | move |
| `DomainModeling/Lowering/DomainToCSharpExporter.cs` | folds into `DomainProgramProjection` + a C# target contributor (O2) | **reshape** |
| `DomainModeling/Lowering/IStorageConvention.cs`, `TypeMappingRegistry.cs`, `DomainTypeMapping.cs` | `Meaning/` (session tables) or `Libraries/Storage/` | move |
| `DomainModeling/Lowering/IStorageSyntaxEmitter.cs` | **`src/` (vendor/host assembly)** — decorates host `CompilationUnitNode`s; consumers are `DbContextGenerator`/`MinimalApiGenerator` only. Not `Meaning/`, not in-tree `Libraries/`. | move out of core |
| `DomainModeling/Runtime/DomainExpressionRewriteBase.cs` | **delete** (use analysis `SetNodeReplacement`) or `Lowering/` if it survives as pre-lower | dispose |
| `src/Poly.DslCompiler/DbContextGenerator.cs`, `MinimalApiGenerator.cs`, `HttpFileGenerator.cs` | **stay in `src/`** (vendor/host assemblies), not `Libraries/` | no move |

### 4b — live types without a target home (disposition)

| Type | Disposition |
|------|-------------|
| `Bootstrap/DomainFactory`, built-in primitives | stays (facts factory); not a phase |
| `Queries/DomainQueries` | stays (MCP projection); fold into tools if MCP slims |
| Analysis diagnostic/hint passes (`EffectAnalyzer`, `PolicyConstraintAnalyzer`, `ExpressionTypeAnalyzer`, `RuleCoverageAnalyzer`, `AuthoringSuggestionAnalyzer`, `CrossReferencePass`) | disposition per cleanup-inventory §C (keep fail-closed; merge hint passes; CrossReference warning) — not this doc |
| `RuntimeAnalysisCache`, `BehaviorMetadata`/`BehaviorModel` | `Analysis/` (cache) / MCP projection (Behavior) — home per cleanup-inventory |
| Ontology fact types at module root (`DomainMember`, `DomainObject`, `DomainTypeReference`, root `Constraint`, `StageSubscription`, `SubscriptionEventAccess`) | `Ontology/` (added to §2 tree) | move |

---

## 5. Invariants preserved (the ADR lock, not the M-moves)

1. Shipped ⊆ lowerable.
2. Runtime and emit consume the **same** lowered trees — no consumer lowering flags.
3. No `Comment` / null-from-lowering / `EffectExecutor` / `ExecuteStructured` as shipped meaning.
4. Core seed has no `Program.cs`; CLI flags seed `uses` ids only.
5. MCP simulate = name operation + context + `Interpreter` on that AST.
6. One library type (`Id` + `Register`).
7. Analyze and emit run on the session.

> Invariants 2 and 3 are **not** locally true for store effects until host-ABI lowering (M3/M4) lands. This layout does not change that; the cleanup plan's slice 3 keeps `ExecuteStructured` and says so.

---

## 6. What does not change

- The facts (Ontology) stay immutable records; `Domain.Relationships` stays a computed flatten; mutation stays `DomainEvolution.Apply`.
- The dispatch bases stay — the exhaustive switch that fails compilation when a subtype is added is a good closed-world mechanism (they are *walkers*, not facts).
- The grammar **engine** (`Poly/Grammar/`) stays; `Language/` owns only the product table, parse and print.
- Module boundaries: `Introspection` ↛ `Interpretation`; `DomainModeling` → `Poly.Ast` for lowering (CORE still writes "Syntax"; fix CORE in the same honesty pass).
- `Poly.Mcp` stays a harness that *holds* a `DomainSession`.

---

## 7. Open questions (for agents to evolve)

- **O1** Does `DomainProgramProjection` stay in `Lowering/` (domain→AST is "lowering") or move to a `Projection/` sibling? The lock says "one lowering"; the projection is a *program* shape, not an *operation* body — are they the same phase?
- **O2** Is the C# idiom decoration (`DomainResult<T>`, `Create` factories) a *library* (opt-in `uses csharp`?) or the core seed's entity-module contributor? M1 moves it out of the exporter but does **not** decide where it lands.
- **O3** ~~folder~~ — **closed:** vendors live in `src/` assemblies (per §2/§4); `mysql` stays extra, not a CLI `--dbms` seed. Open remnant: catalog membership (which vendor ids the compiler catalog resolves), not folder.
- **O4** `ContractFill/` — is contract fill a *phase* (like compile) or a *utility*? It is explicitly not session load; naming the home decides whether `ImportedContract` stays in `Ontology/Contract/`.
- **O5** Ordering vs the cleanup plan: which structural moves are *preconditions* (M2 before M1, because emit must run on the session first) vs *independent*?
- **O6** Home of `Dispatch/` (top-level vs `Lowering/` vs `Compile/`): the walkers are shared by lowering, the printer, and the runtime seam — is a top-level `Dispatch/` the right seam, or do they follow their one primary consumer?

---

## 8. How to review / evolve this document

- **Locked, do not reopen here:** the pipeline phases (§1), Domain / `ExtensionCatalog` / `DomainSession` (the three nouns), contract fill ≠ library, and no new plugin host. Reopening any of those means reopening the ADR — do that in `docs/decisions/`, not here.
- **Not locked — target after host-ABI:** M1, M3, M4. They are *consequences* of the invariants, but they only become locally true once host-ABI `CallExternal` lowering exists. Treat them exactly like the cleanup plan's "still a lie after 3."
- **Open for debate:** folder names, §4 mapping, §4b dispositions, and O1–O6.
- **Review trail:** adversarial findings go to `docs/agent/reviews/` following `docs/agent/phenomenal-review.md`. Edits after review must be dated and recorded, mirroring the F1–F11 pattern on this doc.
- **Honesty bar:** this is a *target*. Where the tree disagrees with the target, say so in the cleanup plan's honesty table — do not write this doc as if `EffectExecutor` or the exporter's split walk are already gone.
- **Do not** convert this document into a task suite, do not admit folder renames from it, and do not flip `PIPELINE-STATUS.md` CURRENT. It becomes executable only through a cleanup/migration plan.

---

## 9. Relationship to the cleanup plan

| | This doc | `domainmodeling-vision-cleanup-2026-08-16.md` |
|---|---|---|
| Role | End-state layout | Executable deletion of dual paths |
| Answers | "where does each phase live" | "what do we delete, in what order, with which failing test" |
| Drives | Re-homes (M1–M6) | Slices 1–3 |
| Admitted | Never directly | Via `PIPELINE-STATUS.md` only |

**Cleanup slices map to this doc as follows:**

- Slices 1–2 ≈ **M2** (one door + analyze sees the session). They are the precondition for any folder move.
- Slice 3 ≈ **Comment honesty**, **not** M3. It fails closed on emit and names `ExecuteStructured` as the runtime seam; it does **not** deliver one lowering.
- **M1, M3, M4 wait** on host-ABI `CallExternal` lowering + emit-on-the-session. Do not admit them from this doc.
