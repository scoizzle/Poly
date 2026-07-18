# DomainModeling product roadmap (milestones)

**Status:** Active (milestones index)  
**Last Updated:** 2026-07-17
**Purpose:** High-level milestone status only.
**Day-to-day work:** [`domainmodeling-next-phase.md`](domainmodeling-next-phase.md)
**Phase 1a archive:** [`dsl-sync-toward-phase1.md`](dsl-sync-toward-phase1.md)

---

## Current product path

```text
Domain (immutable) → DomainEvolution → DomainExpression lower → Syntax AST
  → analysis / node replacement → DirectVmAbiEmitter → VM
MCP / direct API as thin consumers
```

- **No** product µop / primitive IR path  
- **No** V2 (`Poly.Data.Modeling` deleted)  
- **M2** first-consumer vertical slice (structure + policy API + MCP policy) **Done**

---

## Milestones

| Milestone | Status |
|-----------|--------|
| **M1 — Foundation** | ✅ Evolution, proofs, analysis gate |
| **M2 — First consumer** | ✅ Done 2026-07-12 — structure + Person policy + MCP add/evaluate; suite green |
| **M3 — V2 freeze** | ✅ Done |
| **M4 — V2 delete** | ✅ Done |

---

## What next

| Priority | Work | Where | Status |
|----------|------|--------|--------|
| 1 | **Phase 2: Spawn-and-wire** | [`domainmodeling-next-phase.md`](domainmodeling-next-phase.md) | **Complete** |
| 2 | **Phase 3 thin: oracle + A-lite + DSL guide** | [`mcp-phase3-oracle-surface.md`](mcp-phase3-oracle-surface.md) · expansion §0 | **Complete** (V0/S0/A/G) |
| 3 | **MCP dogfood orchestration** | [`mcp-dogfood-orchestrator.md`](mcp-dogfood-orchestrator.md) | **Complete** — [DOGFOOD-REPORT](agent-summaries/dogfood/DOGFOOD-REPORT-20260718.md) |
| 4 | **Runtime MCP thin vertical (RT)** | [`mcp-phase3-oracle-surface.md`](mcp-phase3-oracle-surface.md) **§6c** · expansion §0 | **Complete** — Suite **1354** green; create_instance / call_action / store shipped |
| 5 | RT′ cheap residuals | suggestion discoverability; `actor` parse message | Optional — pain-driven only |
| 6 | Effect-micro / builder rename / V1 | expansion §0 | **Pull-only** (C1 DSL green; C2 is library naming) |
| 7 | **Host-consumable backends** (C# → MSIL → **container images**) | phase3 **§6d** | **Post–Phase 3** — ops often want registry pull; not current |
| 8 | Event authoring tools | — | **Never** (stage transition path) |

## Archived migration material

Completed/superseded workstreams, WP/ws micro-tasks, and µop-era WS8 docs:

**[`../archive/v2-to-v3-migration/README.md`](../archive/v2-to-v3-migration/README.md)**

Do **not** claim WS8 Phase B, WP7 port, or µop pipeline work from archive.

---

## Quality bar (still applies)

| Focus | Meaning |
|-------|---------|
| Correctness | Analysis-gated evolution, honest diagnostics, VM-primary policy when claimed |
| Composition | Direct API + thin MCP |
| Tests | Direct API first; MCP smoke second |
| Natural names | Prefer what-it-is over V3 migration labels (see naming cleanup) |

Platform map: [`docs/CORE.md`](../../CORE.md).
