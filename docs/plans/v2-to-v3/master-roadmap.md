# DomainModeling product roadmap (milestones)

**Status:** Active (milestones index)  
**Last Updated:** 2026-09-03  
**Purpose:** High-level milestone status only.  
**Day-to-day work:** **one admitted suite**. **CURRENT truth:** [`../simple-agent-tasks/PIPELINE-STATUS.md`](../simple-agent-tasks/PIPELINE-STATUS.md). This file does not copy that pick.  
**Completed suites (archived):** [`domainmodeling-completed-2026-08`](../archive/domainmodeling-completed-2026-08/README.md) (`qe` · `vs` · `spe` · `das` · `dacr` · `apm` · `dar` · `dau`) · infra under bar [`../archive/infrastructure-pass/README.md`](../archive/infrastructure-pass/README.md) · late-August [`../archive/completed-2026-08-late/README.md`](../archive/completed-2026-08-late/README.md)

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
| 17 | Infrastructure Groups 1–7 | [`../archive/completed-2026-08-late/infrastructure-pass-NEXT.md`](../archive/completed-2026-08-late/infrastructure-pass-NEXT.md) | **Complete** under bar |
| — | SPE (export peer · entity when · owned policies) · peer `as` · store-aware `Rel exists` | SPE suite · commits | **Complete** |
| — | DAS catalog / monopath analysis | DAS | **Complete** |
| 18 | Q4 aggregates / date ops | query · absorption P1 | **Parked / lower priority** — dates not the next ship bet |
| 19 | Infra Bar B / RestApiSurface / StorageAccess | infra NEXT | **Pull** |
| 20 | E5 micro-tools / dogfood | effect · dogfood | **Parked** until admitted |
| 21 | E3 / L* / events | effect · expansion | **Pull / post–P3 / never** |

### Agent pick

Admission is only [`../simple-agent-tasks/PIPELINE-STATUS.md`](../simple-agent-tasks/PIPELINE-STATUS.md). This file does not copy that pick.

**Focus:** `CURRENT` is `(none)`. Do not invent a second CURRENT. Do not admit a parked suite from this roadmap.
---

## Archived material

| Archive | Contents |
|---------|----------|
| [`../archive/completed-2026-08-late/`](../archive/completed-2026-08-late/README.md) | gpure/mcp-minify/ile/pack-1/rewrite-to-master/grammar-revision/dead-dual/vision-cleanup |
| [`../archive/probes-2026-08/`](../archive/probes-2026-08/README.md) | Historical discovery / fleet-eval probes |
| [`../archive/experiments/`](../archive/experiments/README.md) | Speculative specs (not product DSL) |
| [`../archive/completed-2026-08-mid/`](../archive/completed-2026-08-mid/README.md) | amu/coh/p2–p4/dogfood/grammar/MCP expansion (2026-08) |
| [`../archive/domainmodeling-completed-2026-08/`](../archive/domainmodeling-completed-2026-08/README.md) | apm/das/dacr/dar/dau/spe/qe/vs |
| [`../archive/infrastructure-pass/`](../archive/infrastructure-pass/README.md) | Infra suite design + `ip-*` tasks + review trail |
| [`../archive/v2-to-v3-migration/`](../archive/v2-to-v3-migration/README.md) | Migration micro-tasks / workstreams |
| [`../archive/interpretation/`](../archive/interpretation/README.md) | Superseded Interpretation plans |

Do **not** execute archive work without an explicit re-open against `docs/CORE.md`.
