# Poly.DomainModeling

Immutable domain facts, evolution, analysis, and lower-to-AST. Product surface is `.poly`.

**Platform map:** [`docs/CORE.md`](../../docs/CORE.md).  
**Metadata and artifacts:** [`docs/plans/domainmodeling-metadata-artifact-catalog-2026-08-15.md`](../../docs/plans/domainmodeling-metadata-artifact-catalog-2026-08-15.md).

## Three nouns

1. **Domain** — facts only (types, navs, contracts, `uses` ids). It does not load libraries.
2. **Catalog** (`ExtensionCatalog`) — which libraries this process knows (`temporal`, `storage`, `sqlite`, …).
3. **Session** (`DomainSession`) — this unit’s concept bindings: meaning, folds, type maps, artifacts. Poly’s spell is closed.

`.poly` is one language. `uses foo` loads Foo’s concepts (what `Now` or `column` means), not a dialect. Some extensions **bind product doors** (REST) or projections (SQL); they do not give the Domain a `Main`. MCP holds a `DomainSession` and is the **interactive harness** (simulate with supplied context). Another Poly domain is `ImportedContract`, not an extension id.

## How you enter

One door per job. Do not add a fourth assembler.

| You have | Call | Do not |
|----------|------|--------|
| A `Domain` | `DomainSession.Open(domain)` | Re-open from ids beside the domain |
| `.poly` text | `DomainSession.ForSource(poly, seed, catalog)` then `new PolyDslParser(poly, session)` | A parameterless parser |
| Analysis | `session.Analyze(domain)` | `DomainModelAnalyzer.Analyze` (tests/runtime leftovers only) |
| Mutation | `new DomainEvolution(domain).Apply(changes, session)` | Apply without a session when maps matter |
| CLI emit | `new DslCompiler().Compile(poly, mode, dbms)` | `CreateInputs` / a second session inside compile |

A library is `Id` + `Register` (concepts, not new productions). Duplicate ids fail closed. Agent lock: [`docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md`](../../docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md).

## Quick start

```csharp
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Packs;

Domain domain = DomainFactory.Create("Orders"); // seeds uses temporal
var session = DomainSession.Open(domain);

var result = new DomainEvolution(domain).Evolve()
    .AddEntity("Order")
    .AddPropertyToEntity("Order", new Property("Status", new DomainTypeReference("Text"), []))
    .Apply(session: session);

if (result.Succeeded)
    domain = result.Root;
```

### Query

```csharp
using Poly.DomainModeling.Queries;

var overview = DomainQueries.Overview(domain);
var entity = DomainQueries.GetEntity(domain, "Order");
```

### Rules

1. Every `Apply()` returns a new `Domain`. The original is untouched.
2. Evolution is gated by domain analysis. Failure rolls back.
3. MCP owns the tool conversation (`Poly.Mcp`). `DomainSession` is the loaded-library context, not the MCP session.
4. Prefer `DomainSemanticLookupExtensions` over tree scans. Runtime requires `DomainCatalogMetadata`.
5. C# / program Syntax IR is produced at export (`DomainProgramProjection.ToSyntax`), not mid-pipeline — a **module** of types and operations, not a process.
6. Shipped DSL/effects must lower to a complete operation AST. Do not add `Comment` / `EffectExecutor` / consumer lowering flags as product meaning.
7. Product hosts (REST, …) are opt-in extensions. Core does not emit `Program.cs`.

### Analysis pipeline

```text
Well-formed  →  Catalog (first metadata)  →  Derive (capability, required, topology, storage, …)
                     │
                     ▼
              DomainCatalogMetadata — later passes read this
```

## Directory overview

Layout matches [`docs/plans/domainmodeling-target-architecture-2026-08-16.md`](../../docs/plans/domainmodeling-target-architecture-2026-08-16.md). Namespaces follow folders: facts in `Ontology` (and `Ontology.Contract` / `Constraints` / `Effects` / `Bootstrap`), walkers in `Dispatch`, instances in `Runtime`. Do not `global using` `Ontology` in the core `Poly` assembly — `Action`/`Add`/`ValueType`/`PrimitiveType` collide with `System` and `Poly.Ast`. Sibling folders use explicit usings plus aliases.

| Directory | Purpose |
|-----------|---------|
| `Ontology/` | Facts: Domain, Entity, effects, constraints, contracts, `Bootstrap/` |
| `Dispatch/` | Closed-world walkers (`DomainExpressionDispatch`, `EffectDispatch`) |
| `Compile/` | `DomainSession`, `SessionBuilder`, catalog, library + artifact contracts |
| `Language/` | Product `.poly`: tokens, grammar, parser, printer |
| `Meaning/` | Folds, forms, print maps, `ExpressionMeaning`, annotations, type maps |
| `Analysis/` | Domain analyzers on the shared Analysis substrate |
| `Lowering/` | DomainExpression → AST, C# export |
| `Runtime/` | `DomainEntityInstance`, `DomainInstanceStore` |
| `Evolution/` | `DomainEvolution`, `DomainChange` |
| `Queries/` | MCP/query projections |
| `Libraries/` | In-assembly seeds (`Temporal/`, `Storage/`) |
| `ContractFill/` | Another Domain → `ImportedContract` (not session load) |
| `Constraints/` | Constraint types |
| `Effects/` | Effect types |

## Decisions

- [docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md](../../docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md)
- [docs/decisions/2026-08-14-domain-libraries.md](../../docs/decisions/2026-08-14-domain-libraries.md)
- [docs/decisions/2026-05-31-immutable-core-domain-modeling.md](../../docs/decisions/2026-05-31-immutable-core-domain-modeling.md)
