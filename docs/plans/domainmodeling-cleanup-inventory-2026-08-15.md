# DomainModeling cleanup inventory

**Date:** 2026-08-15  
**Ask:** nothing is sacred (zero customers). Identify excess complexity for clarity and reliability.  
**Not this doc:** a rewrite of Grammar or the VM. The product path stays `.poly` → Domain facts → session load → analyze → lower → export/VM.

**Progress 2026-08-17:** A (session door) including Host nouns: `DomainHost*` / `DomainParserInputs` / `FromInputs` / `failOnUnknown` gone; `IDomainLibrary.Register(SessionBuilder)`; `session.Analyze` is Evolution/MCP/compiler. B (`*.Default` gone, `*Defaults.Add*` gone, MCP holds `DomainSession` only) and C (`BehaviorPass` deleted — project at emit/read) and F (`IContractProducer` deleted) and G (DomainModeling README + CORE + guide) landed. Slice 3: `Comment` is not emit meaning. Remaining: `CreateInputs` third assembler; lint-pass merge + catalog dual; D/E megaclass shrink; host-ABI one-lowering.

---

## Target after cleanup (three nouns)

1. **Domain** — facts only (types, navs, contracts, `uses` ids). No `ResolveHost`.
2. **Catalog** — which libraries this process knows (`temporal`, `storage`, `sqlite`, …).
3. **Session** — Domain + those libraries loaded once: Grammar `Language`, folds, meaning, type maps. Parse, print, analyze, lower, export, runtime all take this.

A library is `Id` + `Register` onto the session’s builder. MCP holds a session. Compiler opens one per compile.

Everything else in the composition layer is a leftover name from an unfinished refactor.

---

## A. Composition layer — too many doors (delete first)

These teach the wrong story. Cheap, high clarity.

| Excess | What it is now | Cleanup |
|--------|----------------|---------|
| `Domain.ResolveHost()` | Fact record loads tables | Delete. Only `DomainSession.Open` / catalog |
| `DomainHost` + `DomainHostBuilder` + `HostSurfaces` | Frozen tables + builder + a view of the builder | Fold into `DomainSession`. One type holds Language + Meaning + Analysis inputs |
| `DomainParserInputs` / `DomainAnalysisInputs` | Split halves of the host | Fields on the session, or gone |
| `DomainHostBuilder.Create()` / `WithStorageFacets()` | Product shortcuts beside the catalog | `catalog.Resolve` / seed lists only |
| `*Defaults.AddSqliteDefaults()` | Wrapper around `Load(SqliteLibrary)` | Delete; call `Load` |
| Folder `Packs/`, `SqlitePack.cs`, tests `*Pack*` | Libraries still named packs | Rename to what they are |
| `DbmsPack` enum | CLI alias that is not a library | `--dbms sqlite` seeds id `sqlite` |
| MCP `ParserInputs` **and** `Modeling` | Two snapshots of the same tables | State holds `DomainSession` only |
| `DomainTools` still `domain.ResolveHost().Parser` for print | Bypasses session | Printer takes `state.Modeling` |
| `DomainSession.FromInputs` rebuilding Temporal meaning via `Create()` | Side door | Open from ids only |
| Stale README (“V3”, “no workspace here”) | Lies next to `DomainSession` | Rewrite to Domain / catalog / session |

**Done when:** `rg ResolveHost\\|DomainHostBuilder\\|HostSurfaces\\|IDomainPack\\|DomainInputSet` in product code is empty.

---

## B. Expression tables — still a kitchen sink

The cycle is: forms + fold + meaning. The code still has a registry that is all of those plus leftovers.

| Excess | Cleanup |
|--------|---------|
| `ExpressionFormRegistry` (contributors + print maps + binary folds + folds) | Session fields: grammar contribute, `ExpressionFoldTable`, print mappings, one `TrySpecializeBinary` |
| `IBinaryExpressionFold` as a named plugin type | Function on the session (already IR-level) |
| `DurationForm` static class | `Duration.TryParseUnit` on the IR type |
| `TemporalDispatchRegistration` (391 lines of handlers) | Stay as Temporal’s meaning; do not add a sixth registry |
| `ExpressionDispatchRegistry.Default` (still on the type, unused) | Delete `Default` so it cannot come back |
| Same for `ExpressionTypeCheckRegistry.Default`, `ExpressionDefaultResolverRegistry.Default` | Delete |

**Done when:** a library registers in one method on the session; no `Default` statics.

---

## C. Analysis — 24 passes, many do not earn a type

Pipeline comment already admits lint-only: Structural, PolicyConstraint, Effect, ConstraintQuality, RuleCoverage, ContractIntegration, Subscription, AuthoringSuggestion. Several of those **do** fail-close product (Structural, Effect, Subscription). The lie is calling all of them “lint.”

| Keep as separate (export/runtime reads the bag or must fail closed) | Merge or drop |
|--------------------------------------------------------------------|----------------|
| Structural (well-formed) | AuthoringSuggestion (hints) |
| Semantic + **one** catalog (not DTLM *and* catalog) | RuleCoverage (hints) |
| Expression types | ConstraintQuality (satisfiability) — merge into expression/constraint check |
| Runtime contracts (subscription plan) | CrossReference (cycle **warning**; metadata had zero consumers) |
| Capability (effective policies) | BehaviorPass — DTO over Capability; compiler/MCP read actions + capability |
| Effect facts + fail-closed Effect/Invariant | StoragePass wrapping StorageAnalyzer — one type |
| Storage (if emitting) | `UseDomainModelValidation()` alias |

`PolicyConstraintAnalyzer` and `EffectAnalyzer` are huge and named “lint” but they are the fail-closed surface for policies/effects. Do **not** delete; do **not** split for elegance. After a lint merge, if EffectAnalyzer/EffectInvariant overlap, delete the overlap only.

**Done when:** pipeline registers ≤ ~10 product passes; `BehaviorPass` gone; one published name catalog.

---

## D. Parse/print — two printers, one RD language leftover

| Excess | Cleanup |
|--------|---------|
| `PolyDslParser` 1528 + `DslExpressionParser` 271 | Leave. Shrink only when a form moves to MatchRule+fold (ident follow-on, effects). Do not rewrite the document parser “for Grammar purity.” |
| `DomainDslPrinter` 812 `StringBuilder` for entity/stage/effect | **Not** a library blocker. Do when header/`uses` pattern is the only Grammar print and agents feel drift. Optional. |
| `DomainProgramProjection` that delegates to the exporter | Finish the move **or** delete the façade |
| Dual `DslGrammar.For` / `LanguageFor` vs session `Language` | Session is the only `Language` factory |

---

## E. Megaclasses — do not “clean,” delete overlap only

| File | Lines | Stance |
|------|------:|--------|
| `DomainToCSharpExporter` | 1839 | Product emit. Split only if projection façade dies |
| `DomainEntityInstance` | 1648 | Runtime. Leave |
| `PolyDslParser` | 1528 | Leave (see D) |
| `EffectAnalyzer` | 1471 | Leave until overlap with Invariant/Facts is deleted |
| `DomainChange` | 1210 / ~60 records | Agent API. Audit MCP emitters; delete change types with **zero** apply path |
| `DomainTools` | 1516 | Thin adapter that grew. After BehaviorPass dies, describe tools read catalog + capability |
| `MinimalApiGenerator` | 1085 | One extra walk. Next delete is sharing a walk with the exporter, not a visitor |
| `DomainQueries` | 287 | MCP-only projection. Keep or inline into tools — no second query model |

---

## F. One-consumer “platforms”

| Type | Consumers | Cleanup |
|------|-----------|---------|
| `IArtifactContributor` | compiler + one Minimal API type | Ordinary method on the compiler, or keep the one interface if it stays one method |
| `IContractProducer` | `InternalDomainProducer` only | Inline into `DomainSuite` (or `Session.FillInternal`) and delete the interface |
| `DomainSuite` | contract fill tests + a few compiler tests | Fine as “named Domain map.” Do not grow into a second session |

---

## G. Docs — second codebase

~587 plan files. `e2e-*` / `pack-*` / stale “IDomainPack” / `DomainInputSet` paragraphs still teach the old story. `DomainModeling/README.md` still says V3 and “no workspace here.”

**Cleanup:** archive parked task farms; one README for DomainModeling that matches Domain / catalog / session; do not keep pack-host as the live composition story.

---

## H. Do not delete (the product)

These are not excess. Removing them would be a new product, not a cleanup.

- Domain as immutable facts + `Extensions`
- Evolution + `DomainChange` (additive agent mutation)
- Analyze before export/runtime (fail closed)
- Grammar as form table (Matcher + Printer + builder)
- Lower DomainExpression → generic AST → VM
- `ImportedContract` as another unit
- Temporal as language default **seed** (meaning stays session-scoped)

---

## Suggested order (clarity first)

1. **A** — one session noun; kill ResolveHost / Host / Create shortcuts / MCP dual snapshot  
2. **B** — one register method; delete `Default` statics  
3. **C** — drop BehaviorPass + lint merge + catalog dual  
4. **G** — docs match the three nouns  
5. **F** — inline one-consumer interfaces  
6. **D/E** only when a megaclass blocks a delete from 1–3  

Stop when a newcomer can implement `uses Foo` by reading Domain, Session, and one Temporal file.
