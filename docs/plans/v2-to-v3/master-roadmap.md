# DomainModeling product roadmap (milestones)

**Status:** Active (milestones index)  
**Last Updated:** 2026-07-24  
**Purpose:** High-level milestone status only.  
**Day-to-day work:** dogfood + pull items on effect/query/infra NEXT files — **not** reopening completed simple-agent queues.  
**Completed suites:** [`simple-agent-tasks/qe-README.md`](simple-agent-tasks/qe-README.md) · [`simple-agent-tasks/vs-README.md`](simple-agent-tasks/vs-README.md) · [`../infrastructure-pass-NEXT.md`](../infrastructure-pass-NEXT.md)

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
| 18 | Q4 aggregates / date ops | query | **Pull** |
| 19 | Infra Bar B / RestApiSurface / StorageAccess | infra NEXT | **Pull** |
| 20 | E5 micro-tools / dogfood | effect · dogfood orchestrator | **Pull / dogfood** |
| 21 | E3 / L\* / events | effect · expansion | **Pull / post–P3 / never** |

### Agent pick (one line)

```text
DONE:    M1–M4; Q1′+Q3′+link_instances; infra IR Groups 1–7 under bar
CURRENT: APM.A1 metadata bridge — [`apm-README.md`](../simple-agent-tasks/apm-README.md)
PULL:    Q4; dates; Bar B; RestApiSurface; unlink MCP; E5 micro-tools
```

**Honest product claim today:** Path-prefix / exists / where **and** Q3′ quantifiers are authorable **and** evaluable (store-linked `EvaluatePolicy`). MCP: `create_instance` → `link_instances` → `evaluate_policy(instanceId=…)`. DSL has **no** `link` keyword (`create in Rel` for spawn-and-wire). JSON policies still local-only. Codegen DbContext/Program via Syntax IR; `.http` still string.

---

## Archived material

| Archive | Contents |
|---------|----------|
| [`../archive/infrastructure-pass/`](../archive/infrastructure-pass/README.md) | Infra suite design + `ip-*` tasks + review trail |
| [`../archive/v2-to-v3-migration/`](../archive/v2-to-v3-migration/README.md) | Migration micro-tasks / workstreams |
| [`../archive/interpretation/`](../archive/interpretation/README.md) | Superseded Interpretation plans |

Do **not** execute archive work without an explicit re-open against `docs/CORE.md`.
