# Plans & Roadmaps

Execution-oriented planning — roadmaps, phase breakdowns, task trackers.

**Not plans:** platform mechanisms → **[`docs/CORE.md`](../CORE.md)**.  
Architectural rationale → **`docs/decisions/`**. Module maps → **`Poly/*/README.md`**.  
**Semantic map / complexity demons** → **[`docs/complexity-semantic-map.md`](../complexity-semantic-map.md)** (facet inventory + duals).  
**Live probe fixtures** → **[`docs/probes/`](../probes/)**. Historical probes → [`archive/probes-2026-08/`](archive/probes-2026-08/README.md).

---

## Admission control

**Trunk is `master`.** Parallel implementation streams are admitted after rewrite-to-master (PR 26). One owner per file; do not serialize the whole repo on a single suite.

| Rule | Meaning |
|------|---------|
| **CURRENT** | Only the Agent pick in [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md) (or `(none)`). |
| **Park before open** | Finish or park the live suite before admitting the next. |
| **Proposals ≠ queues** | Research / design locks stay parked until a suite is solidified and admitted. |
| **Pull ≠ CURRENT** | Available when admitted, not parallel debt. |
| **DONE same PR** | Suite gate Done → update PIPELINE-STATUS + READY-TO-TASK + master-roadmap Agent pick together. |

**CURRENT truth:** [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md) — **CURRENT: `(none)`**. Trunk is `master`. Do not restate a second queue here.  
**Ready suites index:** [`simple-agent-tasks/READY-TO-TASK.md`](simple-agent-tasks/READY-TO-TASK.md)  
**Milestones:** [`v2-to-v3/master-roadmap.md`](v2-to-v3/master-roadmap.md) (mirrors Agent pick)  
**Pre-ship (always-on):** [`v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)

---

## Live agent suites (solidified)

| Suite | README | Plan | Status |
|-------|--------|------|--------|
| **create/create-in** | [`create-create-in-README.md`](simple-agent-tasks/create-create-in-README.md) | [`create-create-in-simulate.md`](create-create-in-simulate.md) | ✅ **DONE** 2026-09-03 — simulate = Interpreter + bound Store. |
| **mut-safety** | [`mut-safety-README.md`](simple-agent-tasks/mut-safety-README.md) | [`mcp-mutation-safety.md`](mcp-mutation-safety.md) | Parked — `THEN` in PIPELINE-STATUS, not admit-next |
| **p1** temporal | [`p1-README.md`](simple-agent-tasks/p1-README.md) | [`p1-temporal-design-lock.md`](p1-temporal-design-lock.md) | Parked until admitted |
| **gcyc** | [`gcyc-README.md`](simple-agent-tasks/gcyc-README.md) | [`grammar-cycle-2026-08-14.md`](grammar-cycle-2026-08-14.md) | Parked — remaining G4 unparse |
| **e2e** | [`e2e-README.md`](simple-agent-tasks/e2e-README.md) | [`domainmodeling-e2e-representation-2026-08-13.md`](domainmodeling-e2e-representation-2026-08-13.md) | Parked — admit one wave |
| **pack-2 / 3** | [`pack-README.md`](simple-agent-tasks/pack-README.md) | [`pack-host-2026-08-13.md`](pack-host-2026-08-13.md) | Parked — phase 1 shipped; extension model superseded |

Queue (`THEN` / `PARKED` / `PULL`) lives only in [`PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md).

**CURRENT:** see [`PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md).

---

## Live design locks / research (not implement suites)

| Doc | Role |
|-----|------|
| [`customer-trust-proof-map.md`](customer-trust-proof-map.md) | Trust claim → proof index |
| [`instance-commit-and-outbox-design-lock.md`](instance-commit-and-outbox-design-lock.md) | Durability / outbox vocabulary |
| [`p1-temporal-research.md`](p1-temporal-research.md) | Temporal research (suite is p1-*) |
| [`domain-dsl-absorption-proposals.md`](domain-dsl-absorption-proposals.md) | P* matrix; pick one suite at a time |
| [`domainmodeling-e2e-representation-2026-08-13.md`](domainmodeling-e2e-representation-2026-08-13.md) | Author → runtime → C# coverage; **parked** |
| [`live-demo-reliability-2026-08-13.md`](live-demo-reliability-2026-08-13.md) | Cut for “compile → run → HTTP” demos |
| [`live-pairing-demo-2026-08-13.md`](live-pairing-demo-2026-08-13.md) | Human + agent progressive authoring → `serve-poly.sh` |
| [`pack-host-2026-08-13.md`](pack-host-2026-08-13.md) | **Not CURRENT.** Phase 1 shipped; extension model superseded. |
| [`dict-sqlite-host-2026-08-30.md`](dict-sqlite-host-2026-08-30.md) | Dict + SQLite host. **Proposal — not CURRENT, not a suite.** |
| [`domainmodeling-simplification-2026-08-14.md`](domainmodeling-simplification-2026-08-14.md) | Deletion-first DomainModeling proposal. **Not CURRENT** |
| [`grammar-cycle-2026-08-14.md`](grammar-cycle-2026-08-14.md) | Grammar as the parse/print cycle. **Not CURRENT** until admitted |
| [`fleet-eval-fixes-2026-08-12.md`](fleet-eval-fixes-2026-08-12.md) | Probe-finding execution checklist (P0–P7). Do not CURRENT beside an overlapping `e2e-*` |

---

## Parked / pull (no suite yet)

| Plan | Unpark when |
|------|-------------|
| [`domainmodeling-e2e-representation-2026-08-13.md`](domainmodeling-e2e-representation-2026-08-13.md) | Explicit slice admit (`e2e-honest` … `e2e-contracts`); do not mega-suite |
| [`fleet-eval-fixes-2026-08-12.md`](fleet-eval-fixes-2026-08-12.md) | Explicit batch admit (P0-0 first) |
| [`ef-and-api-codegen.md`](ef-and-api-codegen.md) | Explicit generation suite admit |
| [`analysis-consuming-lowering.md`](analysis-consuming-lowering.md) | Explicit pick |
| [`post-v2-delete-naming-cleanup.md`](post-v2-delete-naming-cleanup.md) | Idle green tree |
| [`v2-to-v3/effect-surface-completeness.md`](v2-to-v3/effect-surface-completeness.md) | E5/E6 pain |
| [`mcp-domain-inspection-completeness.md`](mcp-domain-inspection-completeness.md) | MCP pain |
| [`domain-migration-poc-plan.md`](domain-migration-poc-plan.md) | Migration consumer |
| [`array-specialization-plan.md`](array-specialization-plan.md) · [`analyzer-improvements.md`](analyzer-improvements.md) · [`ast-types-provider-instance-ergonomics.md`](ast-types-provider-instance-ergonomics.md) | Optional |
| [`platform-velocity-review.md`](platform-velocity-review.md) · [`future-platform-capabilities.md`](future-platform-capabilities.md) | Idea / inventory |
| [`dates-to-pack-2026-08-12.md`](dates-to-pack-2026-08-12.md) | p1-adjacent |
| [`related-entity-stage-gates-research-2026-08-11.md`](related-entity-stage-gates-research-2026-08-11.md) | Research |

---

## Complete (archived — do not reopen)

| Archive | Contents |
|---------|----------|
| [**completed-2026-08-late**](archive/completed-2026-08-late/README.md) | gpure · mcp-minify · ile · pack-1 · rewrite-to-master · grammar-revision · dead-dual · vision-cleanup · executed 2026-08 plans |
| [**probes-2026-08**](archive/probes-2026-08/README.md) | Historical discovery / fleet-eval probe rounds |
| [**experiments**](archive/experiments/README.md) | Speculative specs — not product DSL |
| [**completed-2026-08-mid**](archive/completed-2026-08-mid/README.md) | amu · coh · p2 · p3 · p4 · dogfood · grammar/GIP · MCP expansion/oracle |
| [**domainmodeling-completed-2026-08**](archive/domainmodeling-completed-2026-08/README.md) | apm · das · dacr · dar · dau · spe · qe · vs |
| [**Infrastructure pass**](archive/infrastructure-pass/README.md) | Infra suite |
| [**v2-to-v3-migration**](archive/v2-to-v3-migration/README.md) | V2→V3 workstreams |
| [**Interpretation**](archive/interpretation/README.md) | Historical IR/VM plans |
| [**vision-historical**](archive/vision-historical/README.md) | Old vision docs |

---

## Guardrails (not task lists)

Anti-patterns: [001](anti-pattern-001-duplicate-tree-walks.md), [003](anti-pattern-003-extension-point-accretion.md), [004](anti-pattern-004-interface-new-hiding.md), [005](anti-pattern-005-second-system-effect.md), [007](anti-pattern-007-single-point-dependency.md).  
MCP guiding principles: [v2-to-v3/spikes/mcp-guiding-principles.md](v2-to-v3/spikes/mcp-guiding-principles.md).

---

## Interpretation

| Source of truth | Content |
|-----------------|--------|
| `Poly/Interpretation/README.md` | Pipeline, modules, pass order |
| `docs/decisions/2026-06-08-vm-as-canonical-semantics.md` | VM sole engine |
| `docs/decisions/2026-06-08-domain-lowering-boundary.md` | Domain → generic AST only |

**CURRENT** is `(none)` (see PIPELINE-STATUS). Historical IR/VM plans: [`archive/interpretation/`](archive/interpretation/README.md).
