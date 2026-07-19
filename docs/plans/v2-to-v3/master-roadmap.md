# DomainModeling product roadmap (milestones)

**Status:** Active (milestones index)  
**Last Updated:** 2026-07-18  
**Purpose:** High-level milestone status only.  
**Day-to-day work:** [`dsl-query-surface.md`](dsl-query-surface.md) **§12** · [`simple-agent-tasks/qe-README.md`](simple-agent-tasks/qe-README.md)  
**Phase 1a archive:** [`dsl-sync-toward-phase1.md`](dsl-sync-toward-phase1.md)  
**Phase 2 archive (complete):** [`domainmodeling-next-phase.md`](domainmodeling-next-phase.md)

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
- **Suite baseline:** **1381** (after Q1′′′ partial residuals `3c99221`)

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
| 3 | **MCP dogfood** | [report 1](agent-summaries/dogfood/DOGFOOD-REPORT-20260718.md) · [report 2](agent-summaries/dogfood/DOGFOOD-REPORT-2-20260718.md) | **Complete** (R→RT; post-RT re-rank) |
| 4 | **Runtime MCP (RT)** | phase3 **§6c** | **Complete** — dogfood-2 E2E validated |
| 5 | **RT′ + SA MVP** | phase3 **§6e** | **Complete** `a74af5d` |
| 6 | **SA′ honesty** | phase3 **§6e** | **Complete** — SA′′ all items closed |
| 7 | **Effect surface: E0+E1** | [`effect-surface-completeness.md`](effect-surface-completeness.md) | **Complete** `121cd92` — delete keyword + guide honesty |
| 8 | **E2.1 link decision** | effect-surface decision log | **Complete** — **(a) create-in only**; link DSL deferred |
| 9 | **Q0 → Q1′ authoring** | [`dsl-query-surface.md`](dsl-query-surface.md) §3.1/§4.5 · qe-README | **Complete** `959c6e7` — path-prefix, `Rel exists`, `Rel where` parse/print |
| 10 | **Q1′′′ residual nits** | query **§11** | **Partial** `3c99221` — apply/export, assign LHS/RHS, owned anti-dot; **not** full RT eval |
| 11 | **Q1′′′′ honesty + eval** | query **[§12](dsl-query-surface.md)** | **Complete** — authoring-only claim, analysis DMREL001 for many+property, test renames, owned anti-dot (`1381`)  |
| 12 | **Q3′ any/all/count** | dsl-query-surface | **Pull** after §12 high items (or honest non-goal) |
| 13 | E3 invoke / E1′′′ hygiene | effect-surface | **Pull** |
| 14 | Full effect-micro / V1 / Option A | expansion §0 | **Pull-only** |
| 15 | **Host-consumable** (C# → MSIL → containers) | phase3 **§6d** | **Post–Phase 3** |
| 16 | Event authoring tools | — | **Never** |

### Agent pick (one line)

```text
CURRENT: Q1′′′′.5 nested where; then Q3′ by pain or honest non-goal
THEN:    Q1′′′′.6/.8 hygiene
PULL:    E3b multi-entity invoke; link DSL; L* containers; host I/O effects
```

**Do not** market Q1′ as “related policies evaluate under RT” until §12 Q1′′′′.1 is green or the product claim is narrowed in the guide.

---

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
| Tests | Direct API first; MCP smoke second; **names must match what they assert** |
| Natural names | Prefer what-it-is over V3 migration labels (see naming cleanup) |
| Query honesty | Cross-entity **reads** legal; cross-entity **writes** banned; **no overclaim** of eval or analysis |

Platform map: [`docs/CORE.md`](../../CORE.md).
