# ADR: Frozen core pipeline (AST / Node / Analysis)

**Date:** 2026-09-04  
**Status:** Accepted (agent-facing lock)  
**Deciders:** Primary author  

**Related:** [`docs/CORE.md`](../CORE.md) §0 · [`AGENTS.md`](../../AGENTS.md) · [`2026-08-15-domain-library-extensions-mcp-harness.md`](2026-08-15-domain-library-extensions-mcp-harness.md) · [`2026-08-14-domain-libraries.md`](2026-08-14-domain-libraries.md) · [`2026-06-08-domain-lowering-boundary.md`](2026-06-08-domain-lowering-boundary.md) · [`2026-07-22-persistence-units-medium-facets-pack-syntax-export.md`](2026-07-22-persistence-units-medium-facets-pack-syntax-export.md)

## Context

The repo accumulated a lot of **working consumer machinery** (scratch instance store, C# `Stay.Create`, HTTP Minimal API strings, MCP tool internals, Store job names on `This`). Agents treated that inventory as the architecture and grew dual-paths (`LowerStageTransitions`, Effect-IR walks, consumer-specific flags).

The committed core is smaller: **AST / Node / Analysis**, with libraries that publish bags and artifacts. Hosts, executors, and print targets are replaceable. They must not fork the pipeline.

## Decision

### Frozen (change = platform change)

These are the systematic pipeline. New work composes them. Do not add a parallel Node language, analysis store, rewrite mechanism, or operation menu.

1. **Nodes are the symbolic primary.** `Poly.Ast` (`Node`, `NodeId`). No product-path primitive IR beside AST.
2. **Analysis is the rewrite and fact pipeline.** `Poly.Analysis`: `INodeAnalyzer`, bags (`IAnalysisMetadata` on nodes), **node replacement** (immutable tree). Semantic questions require an `AnalysisResult`. Fail closed when a required bag is missing.
3. **Facts vs bags.** `Domain` is facts (`uses` ids). Analysis publishes **concern bags**. Later passes read bags; they do not rebuild catalogs.
4. **Session loads libraries.** `DomainSession` + `IDomainLibrary.Register` (analyzers, type maps, `IArtifactContributor`). Spell stays closed. Unknown / duplicate ids fail closed.
5. **Shipped ⊆ Node.** A construct ships only if it is a complete, legal generic Syntax tree (or a replacement of one). No `Comment` / `null` lower / second interpreter as shipped meaning. No domain VM opcodes.
6. **Two products from one analyze.**
   - **Operation module:** types + operation bodies as Nodes. Lowering **process** may read bags; the **tree** has none.
   - **Surface bags → host artifacts:** persistence / HTTP / later CLI map the **same catalog** onto a medium. Doors do not invent operations.
7. **No `Main` in core.** Catalog / capability is the operation menu. Product doors are opt-in `uses` libraries.
8. **MCP is the harness**, not a product door and not a second evaluator. Tool `Description` text is usage (what to call, pass, get back), not Interpreter / AST / store types.
9. **New meaning goes through the pipeline:** lower to existing nodes, analyze, and/or **replace nodes**. Not an emitter patch, ABI one-off, or consumer-only lowering flag.

### Current (use; do not reinvent; do not freeze)

Compose these instead of a parallel copy. Replacing them is allowed as a **planned slice** that still exits through frozen seams. Do **not** treat them as load-bearing architecture or grow a sibling path “because emit needs it.”

| Current consumer | Role today |
|---|---|
| `Interpreter` / VM | First executor of Syntax |
| `CSharpGenerator` / `DomainToCSharpExporter` | First print of the module |
| `DomainEntityInstance` / `DomainInstanceStore` | Scratch simulate / session instances |
| Store jobs on `This` (`Create`, `EnsureUnique`, …) | How dictionary `This` calls the directory |
| C# `Stay.Create` / `CreateNav` | Host bind of those jobs inside generated factories |
| `uses http` → Minimal API + `.http` | First process door |
| Introspection CLR provider | First type-system provider |

C# is not a privileged forever target (2026-07-22). Scratch store is not the production directory. Virtual actors / grains / `Insert`+`Link` are not frozen names.

### Forbidden growth

- A consumer-specific lowering flag (`LowerStageTransitions` and the like) in **new** work
- Effect / Domain graph walk as shipped meaning beside the operation AST
- `HostSurface` / unified HTTP-CLI-RPC catalog type before a **second** real door forces it
- MCP as the customer API, or inferred `Main`
- New AST node kinds for one host

## Consequences

- Always-on instructions: [`AGENTS.md`](../../AGENTS.md) **Frozen core**; [`docs/CORE.md`](../CORE.md) §0. Agents and humans follow those, not chat memory of Store/actors/C# print.
- CORE §3 remains “use this, don’t reinvent” for **current** machinery. That is anti-fork, not a freeze of those types.
- Test `FrozenCoreInstructionTests` pins that AGENTS + CORE still state the freeze.
- Transforming current consumers onto named stages: proposal [`docs/plans/pipeline-transformation-2026-09-04.md`](../plans/pipeline-transformation-2026-09-04.md) — not CURRENT.

## Non-goals

- Deleting the scratch store, HTTP door, or C# exporter in this change.
- Admitting a PIPELINE-STATUS CURRENT suite.
