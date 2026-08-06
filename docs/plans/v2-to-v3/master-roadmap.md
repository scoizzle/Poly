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
DONE:    M1–M4; Q1′+Q3′+link; infra IR; domain catalog; peer/entity-level when/owned policies; store-aware Rel exists; dogfood wave-1 + fix G1/G3/HOST; SurfaceExtensionDogfoodTests
CURRENT: dogfood-wave-2 — MCP discovery S4 peer binder → S5 entity-level when → S6 owned+exists+quantifiers
         Queue: simple-agent-tasks/dogfood-README.md
ADMIT:   (wave 2 only) — fix tasks from findings after reports; no parallel amu/P*/cohesion
PARKED:  Absorption P1/P2/P3/P5+; grammar; analysis-consuming lowering residuals; naming V3*; DAU deepen
READY:   pipeline docs/plans/simple-agent-tasks/SUITE-OF-SUITES.md (amu · p4 · coh)
         Copilot: copilot --agent domainmodeling-backlog -p "Execute SUITE-OF-SUITES until complete"
PULL:    Q4; link DSL; E5; Bar B; multi-hop / dates only if wave-2 forces
```

**Honest product claim today:** Path-prefix / exists / where **and** Q3′ quantifiers are authorable **and** evaluable (store-linked `EvaluatePolicy`). Peer binding: `when Rel Stage as name`. Surface extensions: export peer handlers, entity-level `when`, owned policies. MCP: `create_instance` → `link_instances` → `evaluate_policy(instanceId=…)`. DSL has **no** `link` keyword (`create in Rel` for spawn-and-wire). JSON policies still local-only. Codegen DbContext/Program via Syntax IR; `.http` still string. **Temporal DSL authoring** not product. Automated SPE path: [`Poly.Tests/Mcp/SurfaceExtensionDogfoodTests.cs`](../../../Poly.Tests/Mcp/SurfaceExtensionDogfoodTests.cs).

**Focus (2026-08-06):** **Dogfood wave 2** — agent MCP path on peer binder, entity-level when, owned+exists+quantifiers. Report-first; fix second. Do **not** open grammar, temporal, amu, multi-hop, or cohesion in parallel.

---

## Archived material

| Archive | Contents |
|---------|----------|
| [`../archive/domainmodeling-completed-2026-08/`](../archive/domainmodeling-completed-2026-08/README.md) | Finished DM suites + parents (2026-08) |
| [`../archive/infrastructure-pass/`](../archive/infrastructure-pass/README.md) | Infra suite design + `ip-*` tasks + review trail |
| [`../archive/v2-to-v3-migration/`](../archive/v2-to-v3-migration/README.md) | Migration micro-tasks / workstreams |
| [`../archive/interpretation/`](../archive/interpretation/README.md) | Superseded Interpretation plans |

Do **not** execute archive work without an explicit re-open against `docs/CORE.md`.
