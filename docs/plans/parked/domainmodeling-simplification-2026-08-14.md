# Proposal: simplify DomainModeling by deletion

**Date:** 2026-08-14  
**Status:** Proposal (not CURRENT). Admission control: [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md).  
**Lens:** The best next change is the one that **removes a noun, a table, or a pass** without shrinking the agent path (`.poly` → analyze → run / export).  
**Related:** [`../complexity-semantic-map.md`](../complexity-semantic-map.md), [`../decisions/2026-08-14-domain-libraries.md`](../decisions/2026-08-14-domain-libraries.md), [`pack-host-2026-08-13.md`](pack-host-2026-08-13.md)

This is not a rewrite plan and not a plugin-host plan.

---

## 1. What is actually heavy

`Poly/DomainModeling` is ~22.5k lines. That is not “the domain model is rich.” The model records (`Entity`, `Stage`, `Action`, `Effect`, `DomainExpression`) are the cheap part. The cost is **three unfinished refactors stacked on one spine**:

```text
.poly  →  Domain (ids)  →  analyze  →  lower / export / VM
              ↑
     catalogs, hosts, packs, registries, DTO passes
```

| Unfinished refactor | Symptom today |
|---------------------|---------------|
| Pack → Library → `Domain.Extensions` | Folder still `Packs/`. `DomainHostBuilder.Create` / `WithStorageFacets` still seed beside the catalog. `SqlitePack.cs` is a `SqliteLibrary`. Tests named `*Pack*`. `HostSurfaces` is a view of the builder. |
| Grammar-driven spell vs recursive-descent | CORE already calls `ExpressionFormRegistry` a **bridge to delete**. Temporal still ships `IExpressionPrimaryForm` (`NowForm`, `DurationForm`) *and* grammar contributors *and* a binary fold. `PolyDslParser` 1525 + `DomainDslPrinter` 806. |
| Catalog vs leftover analysis bags | 24 pipeline passes. Semantic still publishes `DomainTypeLookupMetadata`; catalog wraps it. `BehaviorPass` is a DTO over `CapabilityAnalyzer`. `StoragePass` is a wrapper around `StorageAnalyzer`. Lint-only passes write nothing anyone else reads. |

Separately, **Temporal meaning is still process-wide**: `TemporalDispatchRegistration` (410 lines) fills five `*.Default` tables on first resolve. Parse/print are per-host; rewrite / lower / type-check / defaults are global. That is the honesty hole the library ADR already named.

Docs are a second codebase: ~587 plan markdown files (was ~455 on 2026-08-08). The just-committed `e2e-*` / `pack-*` task farm is inventory, not a live queue.

---

## 2. What not to delete

These are the product. Simplifying them “into a framework” would be a larger wrong path.

| Keep | Why |
|------|-----|
| `Domain` as compilation unit + `Extensions` ids | One fact list; same Domain + ids ⇒ same artifacts |
| `ExtensionCatalog` as the door | Unknown / duplicate ids fail closed |
| `ImportedContract` (not an extension id) | Another domain is a used unit, not a plugin |
| Evolution + `DomainChange` | Agent mutation is additive facts, not in-place graph edits |
| Fail-closed analyze before export / runtime | Honesty bar |
| Grammar engine (`Poly/Grammar`) | Spell lives in tables, not a second matcher |
| Lower DE → generic AST → VM | No domain opcodes |

Do **not**: introduce MEF, a 12-method plugin, a generic JSON-patch instead of `DomainChange`, or a printer rewrite.

---

## 3. Target shape (fewer nouns)

**Lock (2026-08-14):** the Domain record is statements of fact. A domain session handles the loaded libraries those facts declare.

After deletion, an agent or a compiler should be able to hold this in one breath:

1. A **Domain** is facts: types, relationships, contracts, and `uses` **ids**. It does not resolve or load.
2. A **catalog** is the process door: which libraries this compile/MCP knows.
3. A **DomainSession** is `Open(domain, catalog)`: the Domain + the loaded libraries for `Domain.Extensions` + the tables they registered. Live `IDomainLibrary` instances live here, never on the Domain.
4. Parse, print, analyze, lower, and emit **read the session**. Nothing reads `*.Default`. Nothing calls `Domain.ResolveHost()`.
5. **MCP session** holds a domain session (plus revision, analysis, instances). It is not the loader.
6. Analysis publishes a **catalog + the bags export/runtime actually read**. Lint is one optional pass, or MCP-only.

A new library is: `string Id` + `void Register(DomainSession)` (or the session’s table builder). Not a host, not a pack context, not five static registries.

`DomainHost` / `DomainHostBuilder` / `HostSurfaces` are the current half-name for DomainSession’s tables. Wave A/B delete those nouns in favor of the session.

---

## 4. Waves (delete first, then stop)

Each wave is a small loop: one failing test that the extra noun is gone → smallest delete → green. Park after any wave. Do not admit this as CURRENT beside pack-host / e2e fleets.

### Wave A — Kill leftover names (cheap, do first)

No behavior change. The current tree still teaches the old story.

| Delete or fold | Into |
|----------------|------|
| `Domain.ResolveHost()`, `DomainHost`, `DomainHostBuilder.Create()` / `WithStorageFacets()` | `DomainSession.Open(domain, catalog)`. Facts stay on Domain; load lives on the session. |
| `HostSurfaces` | `IDomainLibrary.Register` onto the session (or its table builder) |
| `UseDomainModelValidation()` alias | `UseDomainModelAnalysisPipeline()` |
| `*Defaults.AddSqliteDefaults()` (and siblings) | `catalog.With(new SqliteLibrary())` / `builder.Load(...)` |
| Folder `Packs/` and types still saying Pack in product comments | `Libraries/` **or** just live next to `Domain.cs`. Rename files `SqlitePack.cs` → already `SqliteLibrary`. Test folder `Packs/` → `Libraries/`. |
| Stale docs that still say `DomainInputSet` / `IDomainPack` / `CreateWithSqlPack` | Point at the ADR or delete the paragraph |

**Done when:** `rg -n 'IDomainPack|DomainInputSet|CreateWithSqlPack|HostSurfaces' Poly/` is empty (tests may keep historical names one release if they assert the old error string — prefer updating).

### Wave B — One host owns meaning (the real library cleanup)

This is the only library work still worth doing.

Today Temporal registers:

| Table | Scope | Job |
|-------|-------|-----|
| `ExpressionFormRegistry` | per host | parse / fold / print / grammar |
| `ExpressionDispatchRegistry<DomainExpression>.Default` | process | rewrite |
| `ExpressionDispatchRegistry<Node>.Default` | process | lower |
| `ExpressionDispatchRegistry<TypeCategory>.Default` | process | infer |
| `ExpressionTypeCheckRegistry.Default` | process | check |
| `ExpressionDefaultResolverRegistry.Default` | process | runtime / export clocks |

**Delete the process `Default`s.** Put the same handlers on the **domain session** (the tables the loaded libraries registered). Dispatch / lowering / type-check take the session, not `domain.ResolveHost` and not `*.Default`. A unit that does not list `temporal` must not see `Now`.

Also delete `TemporalLibrary.EnsureLanguage()` as a public side door.

`TemporalDispatchRegistration` should shrink to “register these handlers on this host” — or disappear into `TemporalLibrary.Register`.

**Done when:** two hosts in one process can disagree about Temporal; E1 tests stay on an empty host without special-casing “don’t load Temporal.”

### Wave C — Delete the parse/print bridges

CORE already promised this. Do not invent new forms on `IExpressionPrimaryForm`.

| Delete after Grammar can | Today’s dual |
|--------------------------|--------------|
| `IExpressionPrimaryForm` + `TryParsePrimary` | `NowForm` / `DurationForm` RD escape vs grammar contributor |
| `IBinaryExpressionFold` if the engine can fold `+`/`-` by pattern | `DateOperationFold` |
| `ExpressionFormRegistry` itself | becomes “the grammar + print mappings already on the host” |

Leave `DomainDslPrinter` / `PolyDslParser` alone except to **stop growing them**. A printer rewrite is a different (worse) project.

**Shipped 2026-08-14 (engine, not Temporal cutover):** `Grammar` is immutable. `GrammarBuilder` is the construct path (`Commit` mutates the builder; `Build` freezes once). `Extend` / `Language.Extend` / `DslGrammar.For` accumulate on one builder. Fold-by-pattern-name (delete `IExpressionPrimaryForm`) is still open.

**Done when:** Temporal primaries are pattern + print mapping only; `IExpressionPrimaryForm` has zero product implementors.

### Wave D — Analysis: 24 passes → ~10

The pipeline is a museum of DAS splits. Product consumers need facts, not pass names.

**Keep as separate passes** (they publish or fail closed for export/runtime):

- `StructuralDomainAnalyzer`
- `SemanticDomainAnalyzer` **or** `DomainCatalogPass` (not both as long-term publishers — see below)
- `ExpressionTypeAnalyzer`
- `RuntimeContractAnalyzer` (subscription dispatch plan)
- `CapabilityAnalyzer` (effective policies / actions)
- `EffectFactsPass` + the **fail-closed** slice of `EffectAnalyzer` / `EffectInvariantAnalyzer`
- `StoragePass` **or** `StorageAnalyzer` (not both)

**Merge into one `AuthoringLint` pass** (or run only from MCP `analyze`, not from `AnalyzeRequiringCatalog`):

- `AuthoringSuggestionAnalyzer` (hints)
- `RuleCoverageAnalyzer` (hints)
- `ConstraintQualityAnalyzer` (satisfiability lint)
- `CrossReferencePass` (cycle **warning**; comment already says its metadata had zero consumers)

**Delete the DTO adapter by making the consumer read the source bag:**

| Adapter | Reads | Consumers should read instead |
|---------|-------|-------------------------------|
| `BehaviorPass` / `BehaviorModel` | `CapabilityAnalyzer` + entity shape | Compiler + `DomainTools` walk actions / `ActionCapabilityMetadata` |
| `StoragePass` wrapping `StorageAnalyzer` | topology + aggregate | One type. Prefer keep `StorageAnalyzer` as a function called from the pass **or** delete the standalone class and have tests go through the pipeline. |

**Collapse the catalog dual:** mid-pipeline still reads `DomainTypeLookupMetadata` from Semantic; product lookups go through `DomainCatalogMetadata`. Pick one published bag. Either Semantic writes the catalog, or every pass after `DomainCatalogPass` uses only the catalog. `DomainSemanticLookupExtensions` already wants this.

Do **not** start by splitting `EffectAnalyzer` (1471) or `ExpressionTypeAnalyzer` (652) into more files. Split only when a **deleted** concern leaves a hole.

**Done when:** `UseDomainModelAnalysisPipeline` registers ≤ 10 product passes; lint is one pass or off the runtime path; `BehaviorPass` is gone; `rg DomainTypeLookupMetadata` is either test-only or gone.

### Wave E — Stop feeding the megaclass, don’t “clean” it

These files are large because they **do the job**. File-split without deletion is churn.

| File | Lines | Deletion stance |
|------|------:|-----------------|
| `DomainToCSharpExporter` | 1836 | `DomainProgramProjection` already admits it still delegates here. Finish the move **or** delete the projection façade. Do not keep both. |
| `DomainEntityInstance` | 1645 | Runtime. Leave it. |
| `PolyDslParser` | 1525 | Shrinks only as Grammar eats Wave C. |
| `EffectAnalyzer` | 1471 | After Wave D, delete overlap with `EffectInvariantAnalyzer` / `EffectFactsPass`. Then stop. |
| `DomainChange` | 1210 / ~60 records | Agent API. Do **not** replace with a generic patch. Audit MCP/`apply_dsl` emitters; delete change types with **zero** apply path if any exist. |
| `MinimalApiGenerator` | 1085 | Lives next to the exporter. Next real delete is “one walk produces both C# domain + HTTP,” not a new visitor. |
| `DomainDslPrinter` | 806 | Leave. |
| `DomainTools` | 1516 | Thin adapter that grew. After Wave D, describe tools read catalog + capability directly; delete Behavior/query DTO glue that only exists to reshape. |
| `DomainQueries` | 287 | Keep as MCP projection **or** inline into tools. Don’t add a second query model. |

### Wave F — Docs are load-bearing complexity

~587 plan files. Admission already says one CURRENT suite.

| Do | Do not |
|----|--------|
| Archive `pack-*` / `e2e-*` task files that are not CURRENT into `docs/plans/archive/` once the suite is parked | Keep them in the live index as if they are the next 80 tickets |
| One README per **admitted** suite | A second CURRENT line for this proposal |
| Update [`complexity-semantic-map.md`](../complexity-semantic-map.md) when a facet dies | Restate CORE inside this file |

This wave can happen any time and is the cheapest cognitive win for the next agent.

---

## 5. Suggested order and what “done” looks like

**Customer outcome:** an agent writes additive `.poly` facts and gets the same working software, with fewer ways to load clocks, fewer analysis DTO names, and no process-wide Temporal.

**Operator outcome:** `Domain.Extensions` + one catalog is the only story in CORE, the DSL guide, the compiler, and MCP.

Recommended sequence if this is ever admitted:

1. **F** (archive stale plans) — hours  
2. **A** (names) — small PR  
3. **B** (host-owned meaning) — the only library PR that still pays for itself  
4. **D** (pass collapse) — biggest DomainModeling LOC win  
5. **C** (Grammar eats RD forms) — only after B, so Temporal has one home  
6. **E** only when a megaclass blocks a delete from B–D

Stop as soon as the nouns fit in §3. Do not complete a “Libraries subsystem.”

---

## 6. Explicit non-goals

- Folder earthquake for its own sake (Wave A rename is allowed; moving Temporal IR out of `DomainModeling` is not)
- Making Temporal optional in the **product seed** (language default stays; meaning just must not be process-global)
- New `IDomainModelingSession` / plugin host
- Completing `IArtifactContributor` / `IContractProducer` as a pack platform — they have one consumer each; leave as ordinary types, or inline `InternalDomainProducer` into `DomainSuite` and delete the interface
- Rewriting export or the VM

---

## 7. Review notes (deletion evidence)

Counted 2026-08-14 on `rewrite/domainmodeling-from-scratch` @ `7877f3da`.

- DomainModeling analysis: **24** `INodeAnalyzer` types in the product pipeline.
- Lint-only (write no bags others read): AuthoringSuggestion, RuleCoverage, ConstraintQuality, PolicyConstraint, CrossReference (warning only).
- DTO / wrapper: BehaviorPass, StoragePass→StorageAnalyzer.
- Temporal meaning tables: **6** (forms + 3 dispatch + checks + defaults), 5 of them process-static.
- Product `IExpressionPrimaryForm` implementors: **2** (`NowForm`, `DurationForm`) — the rest are tests.
- `IArtifactContributor` product implementors: **1**. `IContractProducer`: **1**.
- `DomainHostBuilder.Create` still used by compiler, vendor XML docs, and many tests — catalog is not yet the only door.
