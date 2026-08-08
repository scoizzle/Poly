# DomainModeling product roadmap (milestones)

**Status:** Active (milestones index)  
**Last Updated:** 2026-08-06  
**Purpose:** High-level milestone status only.  
**Day-to-day work:** **one admitted suite** (see Agent pick). Plans index admission rules: [`../README.md`](../README.md).  
**Completed suites (archived):** [`domainmodeling-completed-2026-08`](../archive/domainmodeling-completed-2026-08/README.md) (`qe` · `vs` · `spe` · `das` · `dacr` · `apm` · `dar` · `dau`) · infra under bar [`infrastructure-pass-NEXT.md`](../infrastructure-pass-NEXT.md)

---

## Current product path

```text
Domain (immutable) → DomainEvolution → DomainExpression lower → Syntax AST
  → analysis / node replacement → DirectVmAbiEmitter → VM
MCP / direct API as thin consumers
```

- **No** product µop / primitive IR path  
- **No** V2 (`Poly.Data.Modeling` deleted)  
- **M2** first-consumer vertical slice **Done**  
- **Infrastructure codegen** IR-backed DbContext + Program **Done** (`c5d2220`, `b394a0e`)

---

## Milestones

| Milestone | Status |
|-----------|--------|
| **M1 — Foundation** | ✅ Evolution, proofs, analysis gate |
| **M2 — First consumer** | ✅ Done 2026-07-12 |
| **M3 — V2 freeze** | ✅ Done |
| **M4 — V2 delete** | ✅ Done |

---

## What next

| Priority | Work | Where | Status |
|----------|------|--------|--------|
| 1–8 | Phase 2–3, RT, SA, E0+E1, E2.1 | phase3 · effect-surface | **Complete** |
| 9–15 | Q0→Q1′→Q3′ + residuals | query · `qe-README` | **Complete** |
| 16 | **`link_instances` MCP** | query · `7d067c0` | **Complete** |
| 17 | Infrastructure Groups 1–7 | [`infrastructure-pass-NEXT.md`](../infrastructure-pass-NEXT.md) | **Complete** under bar |
| — | SPE (export peer · entity when · owned policies) · peer `as` · store-aware `Rel exists` | SPE suite · commits | **Complete** |
| — | DAS catalog / monopath analysis | DAS | **Complete** |
| 18 | Q4 aggregates / date ops | query · absorption P1 | **Parked / lower priority** — dates not the next ship bet |
| 19 | Infra Bar B / RestApiSurface / StorageAccess | infra NEXT | **Pull** |
| 20 | E5 micro-tools / dogfood | effect · dogfood | **Parked** until admitted |
| 21 | E3 / L\* / events | effect · expansion | **Pull / post–P3 / never** |

### Agent pick (one line)

```text
DONE:    … p3/p2; GI hybrid cutover + E1 seam; archive completed-2026-08-mid
CURRENT: (none) — admit **gpure** to finish pure Grammar stream
THEN:    gpure → mcp-minify → mut-safety → p1 temporal
PARKED:  outbox lock; multi-assembly DM; actors/schedule
PULL:    E5; EF codegen; naming cleanup
```

**Honest product claim today:** Path-prefix multi-hop; exists/where/Q3′; peer/entity when; catalog; action `-> Entity` returns. **Grammar:** hybrid product path (structure Matcher; expr RD modules + open-form registry). **No** temporal DSL authoring until p1. **No** pure-Grammar product path until gpure gate.

**Focus (2026-08-07):** Prefer **gpure** as sole CURRENT to finish one stream. Ready suites: `docs/plans/simple-agent-tasks/READY-TO-TASK.md`.

---

## Archived material

| Archive | Contents |
|---------|----------|
| [`../archive/completed-2026-08-mid/`](../archive/completed-2026-08-mid/README.md) | amu/coh/p2–p4/dogfood/grammar/MCP expansion (2026-08) |
| [`../archive/domainmodeling-completed-2026-08/`](../archive/domainmodeling-completed-2026-08/README.md) | apm/das/dacr/dar/dau/spe/qe/vs |
| [`../archive/infrastructure-pass/`](../archive/infrastructure-pass/README.md) | Infra suite design + `ip-*` tasks + review trail |
| [`../archive/v2-to-v3-migration/`](../archive/v2-to-v3-migration/README.md) | Migration micro-tasks / workstreams |
| [`../archive/interpretation/`](../archive/interpretation/README.md) | Superseded Interpretation plans |

Do **not** execute archive work without an explicit re-open against `docs/CORE.md`.
