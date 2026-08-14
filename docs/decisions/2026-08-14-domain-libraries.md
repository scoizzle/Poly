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
| Language (clocks) | `TemporalLibrary` | Product default: `DomainHostBuilder.Create()` / `ExtensionCatalog.Core.Language` |
| Storage facets | `StorageFacetLibrary` | Optional: `WithStorageFacets()` — compiler and MCP authoring |
| Persistence | `SqliteLibrary` / `SqlServerLibrary` / `MySqlLibrary` | Host that needs a vendor |
| Fill / emit | `IContractProducer` / `IArtifactContributor` | Unchanged this slice; not `IDomainLibrary` |

Rules:

1. **The domain is the compilation unit.** `Domain.Extensions` lists library ids (`uses temporal`). Another unit is `ImportedContract`.
2. **Resolve from the domain.** Parse (after peek/seed), print, analyze, and emit use `ExtensionCatalog.ResolveHost`.
3. **SDK seed is an additive fact.** New product units get `temporal` (MCP also `storage`) when the source lists no `uses`.
4. **`DbmsPack` seeds a vendor id for compile**; core analysis skips ids it cannot resolve.

Folder homes stay under `Packs/`. No MEF.

## Consequences

- `apply_dsl` / `export_dsl` / `DslCompiler` stamp and honor `uses`.
- Unknown or duplicate extension ids fail closed.
- Process-wide Temporal meaning tables still fill when a unit that lists `temporal` is resolved (per-domain meaning tables are follow-up).
