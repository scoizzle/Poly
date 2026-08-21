# Plans & Roadmaps

Execution-oriented planning — roadmaps, phase breakdowns, task trackers.

**Not plans:** platform mechanisms → **[`docs/CORE.md`](../CORE.md)**.  
Architectural rationale → **`docs/decisions/`**. Module maps → **`Poly/*/README.md`**.  
**Semantic map / complexity demons** → **[`docs/complexity-semantic-map.md`](../complexity-semantic-map.md)** (facet inventory + duals).
---

## Admission control

**One primary implementation workstream at a time.**

| Rule | Meaning |
|------|---------|
| **CURRENT** | Only the Agent pick in [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md) (or `(none)`). |
| **Park before open** | Finish or park the live suite before admitting the next. |
| **Proposals ≠ queues** | Research / design locks stay parked until a suite is solidified and admitted. |
| **Pull ≠ CURRENT** | Available when admitted, not parallel debt. |
| **DONE same PR** | Suite gate Done → update PIPELINE-STATUS + READY-TO-TASK + master-roadmap Agent pick together. |

**CURRENT truth:** [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md) — **`emit-session`**.  
**Vision cleanup (slices 1–3 done; remaining duals parked):** [`domainmodeling-vision-cleanup-2026-08-16.md`](domainmodeling-vision-cleanup-2026-08-16.md). Session four-slot / pack-host Grammar.Extend **superseded** — libraries add `INodeAnalyzer`.  
**Ready suites index:** [`simple-agent-tasks/READY-TO-TASK.md`](simple-agent-tasks/READY-TO-TASK.md)  
**Milestones:** [`v2-to-v3/master-roadmap.md`](v2-to-v3/master-roadmap.md) (mirrors Agent pick)  
**Pre-ship (always-on):** [`v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)

---

## Live agent suites (solidified)

| Suite | README | Plan | Status |
|-------|--------|------|--------|
| **gpure** | [`gpure-README.md`](simple-agent-tasks/gpure-README.md) | [`grammar-pure-end-state.md`](grammar-pure-end-state.md) | ✅ DONE 2026-08-07 |
| **mcp-minify** | [`mcp-minify-README.md`](simple-agent-tasks/mcp-minify-README.md) | [`mcp-catalog-minify.md`](mcp-catalog-minify.md) | ✅ DONE 2026-08-08 |
| **mut-safety** | [`mut-safety-README.md`](simple-agent-tasks/mut-safety-README.md) | [`mcp-mutation-safety.md`](mcp-mutation-safety.md) | **Admit next** |
| **p1** temporal | [`p1-README.md`](simple-agent-tasks/p1-README.md) | [`p1-temporal-design-lock.md`](p1-temporal-design-lock.md) | Ready after mut-safety |

**Suggested order:** ~~gpure → mcp-minify →~~ **mut-safety → p1**.

```bash
copilot --agent plan-suite-until-done -p "Suite: mut-safety. Mode: until-done."
```

**CURRENT:** see [`PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md).
---

## Live design locks / research (not implement suites)

| Doc | Role |
|-----|------|
| [`grammar-pure-end-state.md`](grammar-pure-end-state.md) · **[`gpure-*`](simple-agent-tasks/gpure-README.md)** | Pure Grammar target + suite |
| [`customer-trust-proof-map.md`](customer-trust-proof-map.md) | Trust claim → proof index |
| [`instance-commit-and-outbox-design-lock.md`](instance-commit-and-outbox-design-lock.md) | Durability / outbox vocabulary |
| [`p1-temporal-research.md`](p1-temporal-research.md) | Temporal research (suite is p1-*) |
| [`domain-dsl-absorption-proposals.md`](domain-dsl-absorption-proposals.md) | P* matrix; pick one suite at a time |
| [`domainmodeling-workstream-map.md`](domainmodeling-workstream-map.md) | Orientation inventory |
| [`domainmodeling-e2e-representation-2026-08-13.md`](domainmodeling-e2e-representation-2026-08-13.md) | Author → runtime → C# coverage; **parked** |
| [`simple-agent-tasks/e2e-README.md`](simple-agent-tasks/e2e-README.md) | Fleet implementation queue for that plan (waves + exclusive files) |
| [`live-demo-reliability-2026-08-13.md`](live-demo-reliability-2026-08-13.md) | Cut for “compile → run → HTTP” demos; warehouse/orders first |
| [`live-pairing-demo-2026-08-13.md`](live-pairing-demo-2026-08-13.md) | Human + agent progressive authoring → `serve-poly.sh` |
| [`contract-subdomain-2026-08-13.md`](contract-subdomain-2026-08-13.md) | Contract = used sub-domain; **executed 2026-08-13** |
| [`pack-host-2026-08-13.md`](pack-host-2026-08-13.md) | **Grammar → pack surface → built-in packs**; proposed next admit = phase 1 (TokenWriter + print binders) |
| [`domainmodeling-simplification-2026-08-14.md`](domainmodeling-simplification-2026-08-14.md) | Deletion-first DomainModeling proposal (names, host-owned meaning, pass collapse). **Not CURRENT** |
| [`grammar-cycle-2026-08-14.md`](grammar-cycle-2026-08-14.md) · [`gcyc-*`](simple-agent-tasks/gcyc-README.md) | Grammar as the parse/print cycle; delete RD/print hoops. **Not CURRENT** until admitted |
| [`domainmodeling-cleanup-inventory-2026-08-15.md`](domainmodeling-cleanup-inventory-2026-08-15.md) | Excess-complexity inventory (session vs host, analysis DTO passes, leftover pack names). Identification, not CURRENT |
| [`domainmodeling-metadata-artifact-catalog-2026-08-15.md`](domainmodeling-metadata-artifact-catalog-2026-08-15.md) | Every analysis bag + artifact path; who writes/reads; library extension gaps. Identification, not CURRENT |
| [`domainmodeling-session-is-the-compile-2026-08-15.md`](domainmodeling-session-is-the-compile-2026-08-15.md) | Session is the compile (coordinator). Proposal, not CURRENT |
| [`domainmodeling-extension-architecture-2026-08-15.md`](domainmodeling-extension-architecture-2026-08-15.md) | Best extension arch: four surfaces on DomainSession. Supersedes the extension half. Not CURRENT |
| [`fleet-eval-fixes-2026-08-12.md`](fleet-eval-fixes-2026-08-12.md) | Probe-finding execution checklist (P0–P7). Do not CURRENT beside an overlapping `e2e-*` |

---

## Parked / pull (no suite yet)

| Plan | Unpark when |
|------|-------------|
| ~~[`dead-dual-inventory-2026-08-08.md`](dead-dual-inventory-2026-08-08.md)~~ | ✅ **EXECUTED 2026-08-09** — Validation + Text.Matching deleted; StringView/Parsers kept |
| ~~[`grammar-revision.md`](grammar-revision.md)~~ | ✅ **DONE 2026-08-09** — v2 engine + DSL cutover + printer + review fixes; see doc for final status |
| [`domainmodeling-e2e-representation-2026-08-13.md`](domainmodeling-e2e-representation-2026-08-13.md) | Explicit slice admit (`e2e-honest` … `e2e-contracts`); do not mega-suite; do not overlap a live fleet-eval batch |
| [`fleet-eval-fixes-2026-08-12.md`](fleet-eval-fixes-2026-08-12.md) | Explicit batch admit (P0-0 first). Same bugs as e2e slices P/R/S/3/4/X |
| [`ef-and-api-codegen.md`](ef-and-api-codegen.md) | Explicit generation suite admit |
| [`analysis-consuming-lowering.md`](analysis-consuming-lowering.md) | Explicit pick |
| [`post-v2-delete-naming-cleanup.md`](post-v2-delete-naming-cleanup.md) | Idle green tree |
| [`v2-to-v3/effect-surface-completeness.md`](v2-to-v3/effect-surface-completeness.md) | E5/E6 pain |
| [`mcp-batch-snapshot-efficiency.md`](mcp-batch-snapshot-efficiency.md) · [`mcp-domain-inspection-completeness.md`](mcp-domain-inspection-completeness.md) | MCP pain |
| [`domain-migration-poc-plan.md`](domain-migration-poc-plan.md) | Migration consumer |
| [`dsl-plugin-pipeline-experiment.md`](dsl-plugin-pipeline-experiment.md) · [`domain-plugin-extension-platform.md`](domain-plugin-extension-platform.md) | Historical P1–P4 (shipped). Live workstream: [`pack-host-2026-08-13.md`](pack-host-2026-08-13.md) |
| [`poly-ast-analysis-module-split.md`](poly-ast-analysis-module-split.md) | After product stability |
| [`array-specialization-plan.md`](array-specialization-plan.md) · [`analyzer-improvements.md`](analyzer-improvements.md) · [`ast-types-provider-instance-ergonomics.md`](ast-types-provider-instance-ergonomics.md) | Optional |
| [`platform-velocity-review.md`](platform-velocity-review.md) · [`future-platform-capabilities.md`](future-platform-capabilities.md) | Idea / inventory |
| [`infrastructure-pass-NEXT.md`](infrastructure-pass-NEXT.md) | Infra residual |
| [`domain-modeling-abstraction-gaps.md`](domain-modeling-abstraction-gaps.md) | Gap catalog (coh historical) |

---

## Complete (archived — do not reopen)

| Archive | Contents |
|---------|----------|
| [**completed-2026-08-mid**](archive/completed-2026-08-mid/README.md) | amu · coh · p2 · p3 · p4 · dogfood · grammar/GIP · MCP expansion/oracle/dogfood protocols |
| [**domainmodeling-completed-2026-08**](archive/domainmodeling-completed-2026-08/README.md) | apm · das · dacr · dar · dau · spe · qe · vs |
| [**Infrastructure pass**](archive/infrastructure-pass/README.md) | Infra suite |
| [**v2-to-v3-migration**](archive/v2-to-v3-migration/README.md) | V2→V3 workstreams |
| [**Interpretation**](archive/interpretation/README.md) | Historical IR/VM plans |
| [**vision-historical**](archive/vision-historical/README.md) | Old vision docs |

---

## Guardrails (not task lists)

Anti-patterns: [001](anti-pattern-001-duplicate-tree-walks.md), [003](anti-pattern-003-extension-point-accretion.md), [004](anti-pattern-004-interface-new-hiding.md), [005](anti-pattern-005-second-system-effect.md), [007](anti-pattern-007-single-point-dependency.md).  
MCP guiding principles: [v2-to-v3/spikes/mcp-guiding-principles.md](v2-to-v3/spikes/mcp-guiding-principles.md) (if present).

---

## Interpretation

| Source of truth | Content |
|-----------------|--------|
| `Poly/Interpretation/README.md` | Pipeline, modules, pass order |
| `docs/decisions/2026-06-08-vm-as-canonical-semantics.md` | VM sole engine |
| `docs/decisions/2026-06-08-domain-lowering-boundary.md` | Domain → generic AST only |

**No open Interpretation mega-plan.** Archived: [`archive/interpretation/`](archive/interpretation/README.md).
