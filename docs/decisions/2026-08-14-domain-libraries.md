# ADR: Domain libraries, not packs

**Date:** 2026-08-14  
**Status:** Accepted  
**Deciders:** Primary author  

**Related:** [`2026-07-22-persistence-units-medium-facets-pack-syntax-export.md`](2026-07-22-persistence-units-medium-facets-pack-syntax-export.md), [`../CORE.md`](../CORE.md) §3.6, [`../plans/pack-host-2026-08-13.md`](../plans/pack-host-2026-08-13.md)

## Context

`IDomainPack` flattened four different jobs (language, persistence, contract fill, artifacts) into one noun. `CreateWithSqlPack` was not SQL. Temporal meaning registered via `[ModuleInitializer]` even when the host never loaded Temporal.

## Decision

These things are **libraries**: a referenced assembly that **loads** into a session or compile. Not plugins, not a discovery host.

| Job | Type | When it loads |
|-----|------|----------------|
| Language (clocks) | `TemporalLibrary` | Product seed `uses temporal` / `ExtensionCatalog.Core.Language` |
| Storage facets | `StorageFacetLibrary` | Authoring seed `storage` — compiler and MCP |
| Persistence | `SqliteLibrary` / `SqlServerLibrary` / `MySqlLibrary` | Host that needs a vendor |
| Fill | `InternalDomainProducer` | Contract fill from another Domain — not a session library |
| Artifacts | `IArtifactContributor` registered from `IDomainLibrary.Register` | Same load as DSL and analysis |

Rules:

1. **The domain record is statements of fact.** Types, relationships, contracts, and the ids it uses (`uses temporal`). It does not load libraries. Another unit is `ImportedContract`, not an extension id.
2. **A domain session loads what the domain declared.** Given a `Domain` + a catalog, the session holds the resolved libraries and the parse/print/analysis tables they register. Parse, print, analyze, and emit go through the session — never `Domain.ResolveHost()`.
3. **MCP session ≠ domain session.** MCP is the tool conversation (revision, instances, which unit you are editing). It *holds* a domain session. Do not name the library loader `DomainModelingSession`.
4. **SDK seed is an additive fact** on the Domain (`temporal`; MCP also `storage`) when the source lists no `uses`. The session then loads those ids.
5. **`DbmsPack` seeds a vendor id onto the Domain for compile**; a core catalog session skips ids it cannot resolve.

Folder homes stay under `Packs/` until a deletion pass. No MEF. Live `IDomainLibrary` instances live on the session, never on the Domain.

**Amended 2026-08-14:** “Resolve from the domain” was wrong. The Domain only *declares* ids; the session *handles* the load.

## Consequences

- `apply_dsl` / `export_dsl` / `DslCompiler` stamp and honor `uses`.
- Unknown or duplicate extension ids fail closed.
- Process-wide Temporal meaning tables still fill when a unit that lists `temporal` is resolved (per-domain meaning tables are follow-up).
- **Amended 2026-08-15:** Host / entry-point libraries (REST, …) and the MCP harness are locked in [`2026-08-15-domain-library-extensions-mcp-harness.md`](2026-08-15-domain-library-extensions-mcp-harness.md). “Pack” is a leftover folder name; the noun is **extension** / **library**.
