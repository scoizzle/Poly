# Domain plugin / extension platform

**Status:** Design complete (P0 locked) — P1 executable when prioritized; **not** current product pick.  
**Canonical plan:** [`docs/plans/dsl-plugin-pipeline-experiment.md`](dsl-plugin-pipeline-experiment.md) (**rev 3**)  
**Historical research:** [`docs/experiments/domain-plugin-extension-platform.md`](../experiments/domain-plugin-extension-platform.md)

## Stance

- **Core plugin seams** live in `Poly` / DomainModeling (facet IR on `DomainType` + `Property`, parse/print hooks, type maps, storage conventions, authoring pack set).
- **DBMS / target packs** are separate libraries over time (`Poly.Packs.Sql`, `Poly.Packs.Oracle`, …, third-party).
- **Oracle and other major DBMS** are first-class *consumer scenarios*, not types embedded in core.
- Prefer portable positional `column` / `table` annotations; vendor sugar is optional pack syntax.
- Hosts compose packs: **DslCompiler P1+**, **MCP P4+**.
- Product `poly-dsl-guide` stays **core-only** until MCP pack enablement.

## When to execute

1. P0 is **done** (see plan §11).  
2. Land P1–P2 in core when ready to own facet IR (expect evolution/call-site blast radius).  
3. Do not start at assembly catalogs, actors, or per-vendor core constraints.

**Related product pick now:** still MCP dogfood / pull-only expansion unless explicitly reprioritized.
