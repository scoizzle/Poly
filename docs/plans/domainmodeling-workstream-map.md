# DomainModeling workstream map

**Date:** 2026-08-31 (orientation only — not CURRENT)  
**Purpose:** Name every live / parked / dead stream so “middle of many things” becomes one admitted CURRENT.  
**Sources of truth:** [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md) is the sole CURRENT. This map is orientation inventory, not an Agent pick.  
**Completed suites archive:** [`archive/domainmodeling-completed-2026-08/`](archive/domainmodeling-completed-2026-08/README.md)  
**Rule:** **One primary implementation workstream at a time.** Proposals ≠ queues.

---

## Snapshot (honest)

| Lens | State |
|------|--------|
| Product vertical (M1–M4, spawn-and-wire, Q1′/Q3′, link, SPE, DAS catalog monopath) | **Done** |
| CURRENT (agent pick) | **`create/create-in`** — see [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md). interpretation-language-engine DONE 2026-08-31. dogfood-wave-2 is archived. |
| Feeling of “in the middle” | Many **parked / residual / structural** docs still look active; code is past most of them |

If work feels multi-stream, the fix is **admission**, not more parallel suites.

---

## 1. Completed workstreams (archived)

All suite trees + parent plans: [`archive/domainmodeling-completed-2026-08/`](archive/domainmodeling-completed-2026-08/README.md).

| Stream | Suite | What it was |
|--------|-------|-------------|
| **Foundation + V2 exit** | M1–M4 | Evolution, first consumer, V2 freeze/delete |
| **Spawn-and-wire** | Phase 2 | `create` / `create in`, link fan-out |
| **Query surface** | `qe-*` | Path-prefix, exists, where, Q3′, `link_instances` |
| **Vertical slice / policy MCP** | `vs-*` | M2 finish, policy tools |
| **Analysis pipeline merge** | `apm-*` | Domain passes registered; diagnostics |
| **Authoring context removal** | `dar-*` | Explicit immutable analysis inputs |
| **Downstream analysis consumption** | `dacr-*` | Fail-closed semantic consumers |
| **Domain analysis simplification** | `das-*` | Catalog, effective surface, monopath |
| **Analysis unification** | `dau-*` | Storage/transport always-on (product bar) |
| **Surface extensions** | `spe-*` | Export peer, entity-level `when`, owned policies |
| **Infra under bar** | infrastructure-pass | IR DbContext/Program |
| **Quality / peer followups** | closed deltas | Catalog oracles; peer binding |

Reference capability: [`docs/domainmodeling-capability-inventory.md`](../domainmodeling-capability-inventory.md).

---

## 2. Parked product streams (unpark only by explicit admit)

| Stream | Doc / suite | Customer outcome if admitted | Unpark when |
|--------|-------------|------------------------------|-------------|
| **Dogfood / trust bar** | `mcp-dogfood-*`, protocol | Pain → fix seam or narrow claim | Gaps in SPE/peer/exists path, or new scenario wave |
| **DSL absorption P\*** | `domain-dsl-absorption-proposals.md` | One language slice (not all P*) | **One** P* only; **not** dates-first by default |
| **Temporal / dates authoring** | Absorption **P1** | `Now - 12 days` etc. | Real scenario forces it |
| **when any/all authoring** | Absorption | DSL matches IR quantifiers | Dogfood or explicit pick |
| **Multi-hop nav** | Absorption / PULL | Deeper path-prefix | Dogfood forces |
| **Effect surface E5 / micro-tools** | `effect-surface-completeness` | Author more of existing IR | Explicit effect suite |
| **E3 / L\* / events** | effect expansion | — | Prefer **never** (stage = observable) |
| **Q4 aggregates** | query pull | Aggregates in policies | Explicit; lower priority |
| **unlink MCP** | dogfood / query | Symmetry with link | Dogfood forces |
| **MCP tool expansion** | `mcp-tool-surface-expansion` | Oracle / visibility tools | After dogfood admission |
| **Infra Bar B** | infrastructure NEXT | RestApiSurface / StorageAccess | Explicit pull |
| **DAU codegen deepen** | `dau-*` | Packs / more emit | Product bar already met — only reopen for real consumer |

---

## 3. Structural / hygiene streams (not product features)

| Stream | Doc | Status | Notes |
|--------|-----|--------|-------|
| **DomainModeling decomposition** | archived `completed-2026-08-mid/` + coh suite | **Done** (coh) | Historical only |
| **Grammar framework integration** | archived `completed-2026-08-mid/grammar-integration.md` | **Done** (GI+E1) | Code shipped; plan archived |
| **Analysis-consuming lowering** | `analysis-consuming-lowering.md` | Parked draft | Explicit pick only |
| **DomainAuthoringContext removal plan** | (parent of dar) | **Done via dar** | Plan file may look open; suite closed |
| **Post-V2 naming (`V3*`)** | `post-v2-delete-naming-cleanup.md` | Parked | Idle green tree + explicit pick |
| **Module split Ast/Analysis** | done in CORE | Complete | Pattern reference only |

---

## 4. Why it felt multi-stream (mitigated 2026-08-05)

Finished suites lived next to live indexes. **Mitigation:** archived under `archive/domainmodeling-completed-2026-08/`. Remaining false concurrency risk:

1. **Pull lists** on master-roadmap (Q4, unlink, E5, Bar B) — not CURRENT until admitted.
2. **Experiment DSL** vs product guide — absorption stays parked.
3. **Monolith folder** (`Poly/DomainModeling/`) — cognitive load only; not a parallel queue.

**Enforce:** admission control + Agent pick only.

---

## 5. Recommended organization (operating model)

```text
                    ┌─────────────────────────┐
                    │  master-roadmap         │
                    │  Agent pick = CURRENT   │
                    └───────────┬─────────────┘
                                │ one suite only
              ┌─────────────────┼─────────────────┐
              ▼                 ▼                 ▼
        simple-agent-tasks   dogfood-*        (structural)
        (product suite)      discovery/fix    park until idle
```

| Role | Who owns | Does |
|------|----------|------|
| **CURRENT** | Agent pick line only | Implementation + tests + guide honesty |
| **Parked** | plans README | No code unless unparked |
| **Pull** | listed items | Available when dogfood/customer forces; not parallel debt |
| **Complete** | suite gate `[x]` | Archive mental model; do not re-execute |
| **Always-on** | `pr1` review gate · CORE · dsl-guide | Process + contracts, not features |

---

## 6. Candidate next CURRENT (pick one)

Ranked for **customer outcome** and **low thrash** after 2026-08-04 focus note:

| # | Admit as CURRENT | Size | Why |
|---|------------------|------|-----|
| **A** | **Dogfood wave 2** (new scenarios only; fix suite from findings) | S–M | Trust bar; surfaces shipped SPE/peer/exists without new grammar |
| **B** | **One absorption P\*** (prefer non-temporal: e.g. `when any/all` authoring if IR ready) | M | Language fidelity; single slice → new `simple-agent-tasks/*` |
| **C** | **Q4 or unlink MCP** | M | Only if a named consumer needs it now |
| **D** | **Naming cleanup `V3*`** | S | Hygiene; only on green idle tree |
| **E** | **Decomposition (folders only)** | L | Cognitive load; **zero** product claim change — run only with no product suite |
| **F** | **Stay CURRENT = (none)** | — | Stabilize, dogfood manually, avoid thrash |

**Default recommendation:** **A** or **F**. Do **not** default to temporal/dates (P1). Do **not** open DAU + absorption + grammar together.

---

## 7. Decision checklist (when admitting)

1. [ ] Name **one** suite id or “none”.
2. [ ] Update master-roadmap Agent pick `CURRENT:` line same change.
3. [ ] If product: create/refresh `docs/plans/simple-agent-tasks/<suite>-README.md` with pick order.
4. [ ] Park everything else explicitly (no “also start X”).
5. [ ] Pre-ship via `pr1` before marking Done.

---

## 8. Doc actions (optional hygiene — only if admitted as CURRENT=hygiene)

| Action | Effect |
|--------|--------|
| Banner “COMPLETE — see master-roadmap” on stale next-phase docs | Reduces false open streams |
| Move residual `[~]` notes on closed suites to “ops only” | Stops reopen |
| Link this map from `docs/plans/README.md` | Single orientation entry |

Not required for product progress.

---

## Agent pick (live)

```text
CURRENT: create/create-in
```

This block must match [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md). dogfood-wave-2 is archived. Do not admit work from this map.
