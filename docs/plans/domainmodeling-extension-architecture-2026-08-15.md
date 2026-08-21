# DomainModeling — extension architecture

**Date:** 2026-08-15  
**Status:** **Superseded 2026-08-20** by `emit-session` — libraries add analysis passes; emit consumes bags. Four-slot `Register` (Spell/Meaning/Check/Emit) is the extra plugin host. Not CURRENT.  
**Locks:** Domain = facts; `DomainSession` = compile (core); MCP session ≠ domain session; no MEF; no 12-method plugin.

---

## 1. What we are extending

Poly is **one language**. A Domain does not grow a dialect. `uses foo` loads Foo’s **concepts** into this unit — types, well-known names, annotations, checks, emit — not new syntactic shapes.

Two units can disagree on *whether Now is a clock*, because meaning is session-scoped. They do not disagree on *what a `.poly` file looks like*. Grammar is the product language table, not a plugin host.

An extension can add:

| Surface | Question | Examples |
|---------|----------|----------|
| **Concept** | What exists in this unit? | Temporal clocks, `Duration`, annotation names (`column`), vendor type maps |
| **Meaning** | What do those concepts do? | Rewrite, lower, type-check, defaults — on existing IR / existing spell |
| **Check** | What must be true? | Extra passes; conventions the product storage pass already reads |
| **Emit** | What files come out? | Extra C#, SQL, HTTP — after analysis succeeds |

**Not an extension:** new keywords, new token kinds, new document productions, a token class hierarchy.

Flexibility is **what existing token sequences mean**. `12 days` is already `number` + `identifier`. Temporal registers that pair as a duration (and which idents are units). `Now` is an identifier registered as a clock. `column(...)` is the existing call shape with a registered name. `Name : kind { … }` is the declaration skeleton (`entity`, `enum`, …); a new modeled concept is another `kind`, not a new production. A unit that did not load the concept does not get that meaning — the tokens are unchanged.

Libraries contribute folds/meaning for forms the product language already writes. They do not add shapes.

---

## 2. Three nouns, one compile

```text
Domain            facts (types, navs, contracts, uses ids)
ExtensionCatalog  process: id → IDomainLibrary
DomainSession     closed language instance for this unit
```

`DomainSession` lives in `Poly.DomainModeling` and is the **root**. It is not MCP. MCP holds `(Domain, DomainSession, Analysis, revision, instances)`.

```text
catalog.Resolve(uses or seed)
    → each library.Register(builder)
    → freeze DomainSession

session.Parse(poly)              → DomainChange[]
session.Apply(poly) / evolve     → Domain          (analysis-gated, uses session.Analyze)
session.Analyze(domain)          → AnalysisResult  // session.Passes, not a static cache
session.Print(domain)            → string
session.Emit(domain, analysis)   → files           // session.Artifacts
runtime                          → session.Meaning + analysis bags
```

The session is **frozen tables + operations**. It does not mutate the Domain. `WithDomain` reloads only when `uses` changes.

---

## 3. One library type, four slots

```csharp
public interface IDomainLibrary {
    string Id { get; }
    void Register(DomainSession.Builder session);
}
```

Duplicate `Id` fails closed. A library may fill any subset.

```csharp
public sealed class FooLibrary : IDomainLibrary {
    public string Id => "foo";

    public void Register(DomainSession.Builder s) {
        s.Spell.Grammar(...);
        s.Spell.Fold("expr-primary", "foo", match => ...);
        s.Meaning.Lowering.Register(...);
        s.Check.Add(new FooPass());          // actually runs in session.Analyze
        s.Emit.Add(new FooFiles());          // asked after analysis succeeds
    }
}
```

Authoring: `uses foo`. That is the whole product contract.

**Not** a 12-method interface. **Not** `ILanguageModule` + `IAnalysisModule` + `IEmitModule` (that is a plugin host). One `Register`, four named slots on the builder so the taxonomy is visible.

Core product (entity/stage/action grammar, catalog-first analysis, entity C#) is the **first seed library** (or `RegisterCore` called once at Open). Temporal is the second seed. Sqlite is an id, not a `DbmsPack` civilization.

---

## 4. How each surface composes

### Spell (closed) / Concept (open)

Grammar owns **product** parse/print. Libraries do not extend its productions. They register concepts the existing shapes can already hold (ident, number, string, annotation call).

`session.Analyze` / `Meaning` / `Emit` see those concepts. A unit that omitted `temporal` does not get clock meaning — the *file* still looks like Poly.

### Meaning

One `ExpressionMeaning` per session (rewrite / lower / infer / check / defaults). Empty when the owning library was not loaded. Pack IR then fails closed. No process `Default`.

### Check

`session.Passes` is an ordered list. Open starts with the core seed (structural → catalog → …). Libraries **append**. `INodeAnalyzer.Dependencies` already insert after declared deps — do not invent pass-slot arithmetic.

Duplicate `PassName` fails closed (already true for `AdditionalPasses`).

`session.Analyze(domain)` builds an `Analyzer` from `session.Passes` (cache per session instance if we want, never process-wide).

Type maps and `IStorageConvention` hang on the session; the product `StoragePass` reads them. That is configuration of a core check, not a parallel analyzer host.

### Emit

`IArtifactContributor.Contribute(domain, analysis)` after analysis succeeds; structural failure asks no one.

`session.Emit` is **one loop** over `session.Artifacts`. Entity C# and DbContext are core seed contributors. `CompileMode.All` means the seed included the Minimal API contributor — not a second emit implementation in `DslCompiler`.

---

## 5. Metadata stays a consequence

Check passes publish bags. First metadata is the **name catalog**. Later bags answer new questions (capability, dispatch, storage). Extensions that need a bag write a pass; they do not register “metadata plugins.”

Downstream still: one bag per question. `TryGetStage` / `TryResolveAction` / `GetTypeLookup` read the catalog. Capability is the only effective surface.

---

## 6. Who sits where

| Actor | Job |
|-------|-----|
| `DomainSession` | Compile. Only public coordinator in core. |
| `ExtensionCatalog` | Process-known libraries. |
| `DslCompiler` | CLI: pick seed ids (`sqlite`), `Open`, `Apply`, `Analyze`, `Emit`. |
| `McpSessionState` | Conversation: holds Domain + DomainSession + analysis + instances. |
| `InternalDomainProducer` | Another Domain → `ImportedContract`. Not a library. |

---

## 7. What a newcomer implements

`uses foo` = one class in a referenced assembly, added to the process catalog, `Register` filling the slots they need. They read: Domain, `DomainSession`, Catalog pass, Temporal as the worked example.

They do not read Host, ParserInputs, `DomainModelAnalyzer` static, or `GenerateAllFiles`.

---

## 8. What dies

`DomainHost`, `DomainHostBuilder`, `DomainParserInputs`, `DomainAnalysisInputs` as public nouns.  
Static `DomainModelAnalyzer` cache (or it becomes `session.Analyze`).  
`AddAnalysisPass` that Analyze ignores.  
`GenerateAllFiles` special cases.  
`ExpressionFormRegistry` as the library DSL API.  
`DbmsPack` as a fake library.  
`DomainModelingSession` as a name (MCP collision).

---

## 9. Build order (when admitted)

1. `DomainSession.Builder` with Spell / Meaning / Check / Emit; `Register` takes it; freeze session. Host becomes an internal leftover or disappears.
2. `session.Analyze` from `Check` passes. Core seed registers today’s pipeline. Delete the process cache.
3. `session.Parse` / `Print` / `Emit` on the session. Compiler and MCP call those.
4. Core entity/DbContext contributors. Delete inline emit.
5. Delete remade structure bags and Pack names.

Stop when Foo is `Register` + `uses foo` and there is no second list anywhere.
