# Archived: DomainModeling completed suites (2026-08)

**Archived:** 2026-08-05  
**Reason:** Product and analysis suites below are **done**. Leaving them under live `docs/plans/` made the tree look multi-stream.  
**Do not execute** these as CURRENT work.

## Current path

| Need | Open |
|------|------|
| Agent pick (CURRENT) | [`../../v2-to-v3/master-roadmap.md`](../../v2-to-v3/master-roadmap.md) |
| Plans admission | [`../../README.md`](../../README.md) |
| Workstream map | [`../../domainmodeling-workstream-map.md`](../../domainmodeling-workstream-map.md) |
| Mechanisms | [`../../../CORE.md`](../../../CORE.md) |
| Product DSL | `Poly.Mcp/Docs/poly-dsl-guide.md` |

## What is here

| Area | Contents |
|------|----------|
| **Root (parents)** | APM, DAS future/simplification/catalog, DACR, DAR, SPE, DAU parent plans |
| **`simple-agent-tasks/`** | `apm-*`, `das-*`, `dacr-*`, `dar-*`, `spe-*`, `dau-*`, quality + peer-binding followups |
| **`v2-to-v3/`** | Phase/next-phase, query surface, vertical-slice plan, phase1 DSL sync/grammar |
| **`v2-to-v3/simple-agent-tasks/`** | `qe-*`, `vs-*` |

## Suites (all complete)

| Suite | Theme |
|-------|--------|
| `vs-*` | M2 vertical slice / policy MCP |
| `qe-*` | Query surface Q0–Q3′ + link |
| `apm-*` | Analysis pipeline merge |
| `dar-*` | DomainAuthoringContext removal |
| `dacr-*` | Downstream analysis fail-closed |
| `das-*` | Catalog / monopath analysis |
| `dau-*` | Analysis unification (product bar met) |
| `spe-*` | Surface extensions (export peer, entity when, owned) |

## Rules

1. **Do not** re-open these micro-tasks without an explicit re-admit against master-roadmap.
2. Prefer CORE + capability inventory + dsl-guide over re-reading full suite trees.
3. Design notes here (catalog, future-state) are **historical acceptance** — product behavior lives in code + CORE.
