# DomainModeling product roadmap (milestones)

**Status:** Active (milestones index)  
**Last Updated:** 2026-07-18  
**Purpose:** High-level milestone status only.  
**Day-to-day work:** [`dsl-query-surface.md`](dsl-query-surface.md) **§15** · [`simple-agent-tasks/qe-README.md`](simple-agent-tasks/qe-README.md)  
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
- **Suite baseline:** **1385** (after Q1'''''' `25a79ec` — Rel exists on nav fixed)

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
| 1–8 | Phase 2–3, RT, SA, E0+E1, E2.1 | phase3 · effect-surface | **Complete** |
| 9 | **Q0 → Q1′ authoring** | dsl-query-surface | **Complete** `959c6e7` |
| 10 | **Q1′′′ residual nits** | query §11 | **Partial** `3c99221` |
| 11 | **Q1′′′′ DMREL001 + test honesty** | query §12 | **Complete** `76568a3` |
| 12 | **Q1''''' hygiene** | query §13 | **Complete** `514e21c` |
| 13 | **Q1'''''' Rel exists fix** | query §14 | **Complete** `25a79ec` — N1 nav names accepted for `Rel exists`; goldens green |
| 14 | **Q3′ collection quantifiers** | query · `bb5032b` | **Complete** — `any`/`all`/`none`/`count` DSL + analysis + store-aware eval |
| 15 | **Q3′ residuals** | query · `85d28fe` · [`qe-README`](simple-agent-tasks/qe-README.md) | **Complete** — empty semantics; `evaluate_policy(instanceId=)`; MCP any e2e (library Link) |
| 16 | **`link_instances` MCP** | query **[§18](dsl-query-surface.md)** | **Ready to commit** — golden + validation smokes + E2.1′ + guide §9 |
| 17 | Q4 aggregates / to-one RT eval gaps | query | **Pull** |
| 18 | E3 / effect-micro / L\* / events | effect · expansion · phase3 §6d | **Pull / post–P3 / never** |

### Agent pick (one line)

```text
CURRENT: Commit §18 link_instances batch (exclude demo.http / library.db)
THEN:    Next product work
PULL:    Q4; infra Group 6 production IR ([`ip-README`](../simple-agent-tasks/ip-README.md)); E3b; L*; unlink MCP
```

**Honest product claim today:** Q1′ path-prefix / exists / where **authorable**; **Q3′ quantifiers** authorable **and** evaluable via store-linked `EvaluatePolicy` (MCP: `create_instance` → `link_instances` → `evaluate_policy(instanceId=…)`). **DSL still no `link` keyword** — spawn-and-wire remains `create in Rel`. JSON policies still local-only.

---

## Archived migration material

**[`../archive/v2-to-v3-migration/README.md`](../archive/v2-to-v3-migration/README.md)**

Do **not** claim WS8 Phase B, WP7 port, or µop pipeline work from archive.

---

## Quality bar (still applies)

| Focus | Meaning |
|-------|---------|
| Correctness | Analysis-gated evolution, honest diagnostics, VM-primary when claimed |
| Composition | Direct API + thin MCP |
| Tests | Names match assertions; product forms covered under **analysis** |
| Query honesty | Cross-entity **reads** legal; **writes** banned; authoring ≠ RT eval |

Platform map: [`docs/CORE.md`](../../CORE.md).
