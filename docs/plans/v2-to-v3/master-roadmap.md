# DomainModeling product roadmap (milestones)

**Status:** Active (milestones index)  
**Last Updated:** 2026-09-03  
**Purpose:** High-level milestone status only.  
**Day-to-day work:** **one admitted suite**. **CURRENT truth:** [`../simple-agent-tasks/PIPELINE-STATUS.md`](../simple-agent-tasks/PIPELINE-STATUS.md) (Agent pick below must match).  
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

### Agent pick (one line)

```text
DONE:    … p3/p2; GI; E1; gpure (2026-08-07); mcp-minify (2026-08-08); vision-cleanup 1–3 (2026-08-17); emit-session CompileMode seed-only (2026-08-24); host-ABI PRs 21–24; rewrite-to-master (PR 26); interpretation-language-engine (ile-gate 2026-08-31)
CURRENT: create/create-in
ADMIT:   parallel (exclusive files)
THEN:    MCP mut-safety; Grammar wrap-up; V3 naming
PARKED:  pack-2 IDomainPack; mut-safety; e2e-*; pack-host “packs extend Grammar tables”; session four-slot Meaning/Emit
PULL:    E5; EF codegen; naming cleanup
```

**Honest product claim today:** Path-prefix multi-hop; exists/where/Q3′; peer/entity when; catalog; action `→ Entity` returns. **Grammar:** product parse is Grammar-table-guided (Option A expr ladder + effect heads; printer deferred). **MCP:** DSL-only expressions; unified `add`/`remove` + `apply_dsl`. CompileMode seeds persistence only; HTTP host is `uses http`. **No** temporal DSL authoring until p1.

**Focus (2026-09-03):** CURRENT is create/create-in — simulate the lowered program ([`../create-create-in-simulate.md`](../create-create-in-simulate.md)). Unique Store bind shipped. Do not invent a second CURRENT. Do not admit dict-sqlite or mut-safety.
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
