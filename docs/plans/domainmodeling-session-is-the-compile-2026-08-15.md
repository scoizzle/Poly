# DomainModeling — the session is the compile

**Date:** 2026-08-15  
**Status:** Design lock (proposal). Not CURRENT. Not another hook on `DomainHostBuilder`.  
**Related:** [`2026-08-14-domain-libraries.md`](../decisions/2026-08-14-domain-libraries.md), [`domainmodeling-metadata-artifact-catalog-2026-08-15.md`](domainmodeling-metadata-artifact-catalog-2026-08-15.md)

---

## The flaw

The nouns are right: **Domain** (facts), **Catalog** (known libraries), **Session** (those libraries loaded once).

The *implementation* is several unfinished compositions stacked on each other:

| Layer we added | What it was supposed to be | What it actually is |
|----------------|----------------------------|---------------------|
| `DomainHost` + `DomainHostBuilder` | Load tables | Still the public assembler; Session wraps it |
| `DomainParserInputs` / `DomainAnalysisInputs` | Halves of the host | Extra types for the same tables |
| `DomainModelAnalyzer` static cache | Product analyze | **Cannot see** session `AdditionalPasses` |
| `GenerateAllFiles` + contributor list | Emit | Entity/DbContext inline; host files via libraries |
| `ExpressionFormRegistry` | Library DSL | Kitchen sink beside `Language` + folds + meaning |
| Folder `Packs/`, `DbmsPack` | Libraries | Old name |

Each increment made one door honest (no `ResolveHost`, no `*.Default`, catalog-first metadata, artifacts on `Register`) without making **one compile**. Extensibility still feels half-done because it *is*: load is session-scoped; analyze and most emit are not.

That is not fixed by a twelfth `RegisterX` method.

---

## First principles

A domain modeling platform is a **compiler for one compilation unit**.

1. **A unit is facts plus the libraries it named.**  
   `Domain` holds types, navs, contracts, `uses` ids. It never loads code. Another Poly domain is `ImportedContract`, not an id.

2. **The process knows libraries; the unit selects them.**  
   `ExtensionCatalog`: id → `IDomainLibrary`. Unknown or duplicate id fails closed.

3. **Load happens once and produces a closed world.**  
   That world is the **session**: language (parse/print), meaning, analysis pipeline, artifact emitters. Nothing process-global. MCP’s tool conversation is a different session that *holds* this one.

4. **Every phase of the compile reads that same world.**  
   Parse, print, analyze, lower, export, runtime. If a library is loaded, its grammar, checks, and files are in; if it is not, they are out. No static pipeline that ignores what was loaded.

5. **A library is one type.**  
   `Id` + `Register(SessionBuilder)`. It may add spell, meaning, passes, artifacts. It is not a 12-method plugin and not MEF. Empty contributions are fine (Temporal adds no files).

6. **Metadata is a consequence of passes, not a second plugin.**  
   First metadata = name catalog (what exists). Later bags = derived questions (capability, dispatch, storage). No remake of the catalog.

7. **Core is the first seed, not a side path.**  
   Product DSL + fail-closed analysis are registered the same way as Temporal — a seed list when `uses` is empty — not a special `GenerateAllFiles` civilization beside the library list.

---

## Target architecture

```text
ExtensionCatalog          id → Library
Library                   Id + Register(SessionBuilder)
SessionBuilder            mutable assemble (one type)
Session                   frozen: Language, Meaning, Passes, Artifacts, TypeMaps, …
Domain                    facts + uses ids

Session.Open(domain, catalog)
  1. resolve uses (or seed)
  2. each library.Register(builder)
  3. freeze

session.Parse(source)     → DomainChange[] / Domain
session.Analyze(domain)   → AnalysisResult   // product passes + builder.Passes
session.Print(domain)     → .poly
session.Emit(domain, analysis) → files       // core seed contributors + library contributors
runtime                   reads analysis bags + session meaning
```

**Root type lives in core `Poly.DomainModeling`.** That is today’s `DomainSession`, grown into Parse / Analyze / Print / Emit — not a new coordinator in MCP or the compiler.

Do **not** name it `DomainModelingSession`. MCP already has a session (tool conversation, revision, instances). The ADR lock stands: MCP *holds* a domain session; it is not the same type. `DomainSession` is the compile. `McpSessionState` stays the conversation.

**Gone as public nouns:** `DomainHost`, `DomainHostBuilder`, `DomainParserInputs`, `DomainAnalysisInputs`, `DbmsPack` as a library substitute, `AddAnalysisPass` that Analyze never runs.

**One leftover compiler concern:** CLI `--dbms sqlite` is a **seed id** (`sqlite`), not an enum that bypasses the catalog.

**Contract fill** (`InternalDomainProducer` / `DomainSuite`) stays outside this: it turns another Domain into `ImportedContract`. That is not session load.

---

## What “extends the DSL / analysis / artifacts” means

| Concern | Session field after load | Who adds it |
|---------|--------------------------|-------------|
| Tokens / patterns / print | `Language` | Core seed + library grammar contributors |
| Primary/binary folds | `Folds` | Core + library |
| Rewrite / lower / check / defaults | `Meaning` | Library (Temporal is the example) |
| Type maps, storage conventions | on session | Persistence libraries |
| Analyzer passes | `Passes` | Core seed + library (`AddPass`, duplicate name fails) |
| Output files | `Artifacts` | Core seed (entities, optional DbContext) + library |

`Analyze` = `AnalyzerBuilder` from `session.Passes`, not `DomainModelAnalyzer`’s process-wide cache.

`Emit` = `foreach session.Artifacts: Contribute(domain, analysis)` after analysis succeeds. Entity C# is a core contributor, not a second loop. `CompileMode` chooses **which seed libraries** (or which seed artifacts) are loaded, not a parallel emit implementation.

---

## Grade of the current tree

| Principle | Grade | Evidence |
|-----------|-------|----------|
| Domain is facts | A | `uses` ids; no `ResolveHost` |
| Catalog resolves ids | A | `ExtensionCatalog` |
| Session is the closed world | D | Session exists; Host still builds it; Analyze ignores it |
| One library type | B | `IDomainLibrary` is the type; artifacts can `Register`; analysis passes cannot run |
| Metadata composes | B | Catalog-first; StageByName / MTI / CrossReference still copy |
| Core is a seed | D | Static analyzer + `GenerateAllFiles` |
| No process-global meaning | A | `*.Default` gone |

The increments were correct *local* fixes. They cannot add up to this architecture because **analyze and emit were never moved onto the session**.

---

## How to get there (one rebuild, not twelve hooks)

Do this as **replace the compile**, not as more cleanup inventory.

1. **`SessionBuilder` + freeze `DomainSession`.** Move fields off Host/ParserInputs/AnalysisInputs. `IDomainLibrary.Register(SessionBuilder)`. Catalog `Open` returns a session.
2. **`session.Analyze`.** Product pipeline becomes the default pass list registered by a core seed library (or a `RegisterCore(builder)` called once at Open). Library `AddPass` actually runs. Delete the static cached `DomainModelAnalyzer` or make it `Open(domain).Analyze()`.
3. **`session.Emit`.** One contributor list. Entity types and DbContext become core contributors. Minimal API stays a contributor (mode All = load that contributor). Delete `GenerateAllFiles`’s special cases.
4. **Delete remade structure** (ESM `StageByName`, CrossReference’s private rel index) only after (1)–(2) — they are symptoms.
5. **Rename Packs / DbmsPack** last. Names follow the compile, they do not create it.

Stop when: a Foo library with a keyword, a check, and a file is `Register` plus `uses foo`, and the compiler has no second list.

---

## What we will not do

- MEF, discovery, “plugin host,” 12-method `IDomainLibrary`
- Incremental `RegisterAnalysis` that still hits the static analyzer
- Putting capability or storage columns in the name catalog
- Rewriting Grammar or the VM for this
