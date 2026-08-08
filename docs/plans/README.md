# Plans & Roadmaps

Execution-oriented planning — roadmaps, phase breakdowns, task trackers.

**Not plans:** platform mechanisms → **[`docs/CORE.md`](../CORE.md)**.  
Architectural rationale → **`docs/decisions/`**. Module maps → **`Poly/*/README.md`**.

---

## Admission control

**One primary implementation workstream at a time.**

| Rule | Meaning |
|------|---------|
| **CURRENT** | Only what master-roadmap Agent pick says (or `(none)`). |
| **Park before open** | Finish or park the live suite before admitting the next. |
| **Proposals ≠ queues** | Research / design locks stay parked until a suite is solidified and admitted. |
| **Pull ≠ CURRENT** | Available when admitted, not parallel debt. |

**Agent pick:** [`v2-to-v3/master-roadmap.md`](v2-to-v3/master-roadmap.md)  
**Ready suites index:** [`simple-agent-tasks/READY-TO-TASK.md`](simple-agent-tasks/READY-TO-TASK.md)  
**Pre-ship (always-on):** [`v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)

---

## Live agent suites (solidified)

| Suite | README | Plan | Status |
|-------|--------|------|--------|
| **gpure** | [`gpure-README.md`](simple-agent-tasks/gpure-README.md) | [`grammar-pure-end-state.md`](grammar-pure-end-state.md) | Ready — **prefer CURRENT** (finish pure Grammar) |
| **mcp-minify** | [`mcp-minify-README.md`](simple-agent-tasks/mcp-minify-README.md) | [`mcp-catalog-minify.md`](mcp-catalog-minify.md) | Ready — after gpure |
| **mut-safety** | [`mut-safety-README.md`](simple-agent-tasks/mut-safety-README.md) | [`mcp-mutation-safety.md`](mcp-mutation-safety.md) | Ready — after minify |
| **p1** temporal | [`p1-README.md`](simple-agent-tasks/p1-README.md) | [`p1-temporal-design-lock.md`](p1-temporal-design-lock.md) | Ready — after pure/minify as preferred |

**Suggested order:** **gpure** → mcp-minify → mut-safety → p1.

```bash
copilot --agent plan-suite-until-done -p "Suite: gpure. Mode: until-done."
```

**CURRENT:** see master-roadmap (default none until admit).

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

---

## Parked / pull (no suite yet)

| Plan | Unpark when |
|------|-------------|
| [`ef-and-api-codegen.md`](ef-and-api-codegen.md) | Explicit generation suite admit |
| [`analysis-consuming-lowering.md`](analysis-consuming-lowering.md) | Explicit pick |
| [`post-v2-delete-naming-cleanup.md`](post-v2-delete-naming-cleanup.md) | Idle green tree |
| [`v2-to-v3/effect-surface-completeness.md`](v2-to-v3/effect-surface-completeness.md) | E5/E6 pain |
| [`mcp-batch-snapshot-efficiency.md`](mcp-batch-snapshot-efficiency.md) · [`mcp-domain-inspection-completeness.md`](mcp-domain-inspection-completeness.md) | MCP pain |
| [`domain-migration-poc-plan.md`](domain-migration-poc-plan.md) | Migration consumer |
| [`dsl-plugin-pipeline-experiment.md`](dsl-plugin-pipeline-experiment.md) · [`domain-plugin-extension-platform.md`](domain-plugin-extension-platform.md) | Pack host consumer |
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
