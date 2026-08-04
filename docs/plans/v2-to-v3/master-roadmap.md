# DomainModeling product roadmap (milestones)

**Status:** Active (milestones index)  
**Last Updated:** 2026-08-04  
**Purpose:** High-level milestone status only.  
**Day-to-day work:** **one admitted suite** (see Agent pick). Plans index admission rules: [`../README.md`](../README.md).  
**Completed suites:** [`qe-*`](simple-agent-tasks/qe-README.md) · [`vs-*`](simple-agent-tasks/vs-README.md) · [`spe-*`](../simple-agent-tasks/spe-README.md) · [`../infrastructure-pass-NEXT.md`](../infrastructure-pass-NEXT.md) · DAS / peer-binding / store-aware exists (recent commits)

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
DONE:    M1–M4; Q1′+Q3′+link; infra IR; DAS; SPE peer/entity/owned; store-aware Rel exists; dogfood-fix G1/G3/HOST; SPE MCP dogfood suite (`SpeDogfoodTests`); DAU product parked
CURRENT: (none) — SPE dogfood test expansion landed; admit next only when chosen
ADMIT:   Further dogfood scenarios if gaps; not temporal/dates first
PARKED:  Absorption P1 dates/temporal pack (lower priority); P2–P5+; dogfood S* discovery; grammar; DomainAuthoringContext; analysis-consuming lowering; naming V3*
PULL:    Q4; unlink; E5; Bar B; DAU commit ops; multi-hop only if dogfood forces
```

**Honest product claim today:** Path-prefix / exists / where **and** Q3′ quantifiers are authorable **and** evaluable (store-linked `EvaluatePolicy`). Peer binding: `when Rel Stage as name`. SPE: export peer handlers, entity-level `when`, owned policies. MCP: `create_instance` → `link_instances` → `evaluate_policy(instanceId=…)`. DSL has **no** `link` keyword (`create in Rel` for spawn-and-wire). JSON policies still local-only. Codegen DbContext/Program via Syntax IR; `.http` still string. **Temporal DSL authoring** (`Now - 12 days`) not yet product — **intentionally not the next bet**. SPE agent path is covered by [`Poly.Tests/Mcp/SpeDogfoodTests.cs`](../../../Poly.Tests/Mcp/SpeDogfoodTests.cs).

**Focus (2026-08-04):** Close unfinished-workstream thrash — **one primary only**. SPE surface dogfood tests shipped on MCP path; temporal pack stays parked. Do **not** open grammar, multi-hop, when any/all, and packs in parallel. DAU remains parked.

---

## Archived material

| Archive | Contents |
|---------|----------|
| [`../archive/infrastructure-pass/`](../archive/infrastructure-pass/README.md) | Infra suite design + `ip-*` tasks + review trail |
| [`../archive/v2-to-v3-migration/`](../archive/v2-to-v3-migration/README.md) | Migration micro-tasks / workstreams |
| [`../archive/interpretation/`](../archive/interpretation/README.md) | Superseded Interpretation plans |

Do **not** execute archive work without an explicit re-open against `docs/CORE.md`.
