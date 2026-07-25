# Plans & Roadmaps

Execution-oriented planning — roadmaps, phase breakdowns, task trackers.

**Not plans:** platform mechanisms → **[`docs/CORE.md`](../CORE.md)**.  
Architectural rationale → **`docs/decisions/`**. Module maps → **`Poly/*/README.md`**.

---

## Active (use these)

| Plan | Role |
|------|------|
| [**Master roadmap**](v2-to-v3/master-roadmap.md) | Milestone index + one-line agent pick |
| [**Analysis pipeline merge**](analysis-pipeline-merge.md) | **Complete** — A–E′ closed; 1611 green. [`apm-*`](simple-agent-tasks/apm-README.md) |
| [**Capability inventory**](../domainmodeling-capability-inventory.md) | What ships (reference, not a queue) |
| [**Effect surface completeness**](v2-to-v3/effect-surface-completeness.md) | Effects track — kernel shipped; dogfood / E5 / E6.1 pull |
| [**DSL query surface**](v2-to-v3/dsl-query-surface.md) | **Complete** Q1′+Q3′+`link_instances` — design reference; pull Q4/dates |
| [**MCP dogfood orchestrator**](v2-to-v3/mcp-dogfood-orchestrator.md) | Dogfood loop — [report 1](v2-to-v3/agent-summaries/dogfood/DOGFOOD-REPORT-20260718.md) · [report 2](v2-to-v3/agent-summaries/dogfood/DOGFOOD-REPORT-2-20260718.md) |
| [**MCP tool-surface expansion**](v2-to-v3/mcp-tool-surface-expansion.md) | Backlog §0 — pull-only after dogfood |
| [**Infrastructure pass — NEXT**](infrastructure-pass-NEXT.md) | **Complete** under bar; pull Bar B / RestApi — [archive](archive/infrastructure-pass/README.md) |
| [**Post–V2-delete naming cleanup**](post-v2-delete-naming-cleanup.md) | Drop product `V3*` labels — idle green tree |
| [**Post–system-review correctness hardening**](2026-07-11-review-fix-plan.md) | Mostly Done; optional residuals |

### Simple-agent queues

| Queue | Role |
|-------|------|
| [**`apm-*` (analysis pipeline merge)**](simple-agent-tasks/apm-README.md) | **Primary** — Phase A1→Gate then optional B |
| [**`qe-*` (query/effect)**](v2-to-v3/simple-agent-tasks/qe-README.md) | **Complete** — do not reopen Q0–Q3′; optional hygiene only |
| [**`vs-*` (vertical slice)**](v2-to-v3/simple-agent-tasks/vs-README.md) | Historical M2 — **done**; do not reopen |
| [**`pr1` review gate**](v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md) | Always-on pre-ship process |

---

## Deferred (do not execute yet)

| Plan | Role |
|------|------|
| [**Domain plugin / multi-DBMS packs**](domain-plugin-extension-platform.md) | → [`dsl-plugin-pipeline-experiment.md`](dsl-plugin-pipeline-experiment.md) — P1-ready, not current pick |
| [**`Poly.Ast` + `Poly.Analysis` module split**](poly-ast-analysis-module-split.md) | After product stability |
| [array-specialization-plan.md](array-specialization-plan.md) | Optional emitter work |
| [analyzer-improvements.md](analyzer-improvements.md) | Optional analysis quality |
| [future-platform-capabilities.md](future-platform-capabilities.md) | Idea backlog |

---

## Guardrails (not task lists)

Anti-patterns: [001](anti-pattern-001-duplicate-tree-walks.md), [003](anti-pattern-003-extension-point-accretion.md), [004](anti-pattern-004-interface-new-hiding.md), [005](anti-pattern-005-second-system-effect.md), [007](anti-pattern-007-single-point-dependency.md).  
DomainModeling abstraction gaps: [domain-modeling-abstraction-gaps.md](domain-modeling-abstraction-gaps.md).  
MCP guiding principles: [v2-to-v3/spikes/mcp-guiding-principles.md](v2-to-v3/spikes/mcp-guiding-principles.md).

---

## Interpretation

| Source of truth | Content |
|-----------------|--------|
| `Poly/Interpretation/README.md` | Pipeline, modules, pass order |
| `docs/decisions/2026-06-08-vm-as-canonical-semantics.md` | VM sole engine |
| `docs/decisions/2026-07-04-primitives-as-canonical-ir.md` | Historical title; body = **direct AST→ABI** |
| `docs/decisions/2026-06-08-domain-lowering-boundary.md` | Domain → generic AST only |

**No open Interpretation mega-plan.**

---

## Archived (do not execute)

| Archive | Contents |
|---------|----------|
| [**archive/infrastructure-pass/**](archive/infrastructure-pass/README.md) | Groups 1–7 complete: design, ladder, `ip-*` tasks, review trail |
| [**archive/interpretation/**](archive/interpretation/README.md) | µop/bytecode/tree-walker/primitive IR era |
| [**archive/v2-to-v3-migration/**](archive/v2-to-v3-migration/README.md) | Completed migration micro-tasks / workstreams |
| [**archive/vision-historical/**](archive/vision-historical/README.md) | Vision sketches that contradict shipped execution model |

Also historical but left in place (reference only):  
[`v2-to-v3/vertical-slice-finish-plan.md`](v2-to-v3/vertical-slice-finish-plan.md) · [`v2-to-v3/mcp-phase3-oracle-surface.md`](v2-to-v3/mcp-phase3-oracle-surface.md) · [`v2-to-v3/domainmodeling-next-phase.md`](v2-to-v3/domainmodeling-next-phase.md) · [`v2-to-v3/dsl-sync-toward-phase1.md`](v2-to-v3/dsl-sync-toward-phase1.md)

Agents **must not** implement archive work without an explicit re-open validated against `docs/CORE.md` and current code.

---

## Guidelines

1. **What next?** → [`simple-agent-tasks/apm-README.md`](simple-agent-tasks/apm-README.md) (pipeline merge) · [`v2-to-v3/master-roadmap.md`](v2-to-v3/master-roadmap.md) · dogfood.  
2. Consult `docs/decisions/` and `CORE.md` before significant work.  
3. Do not invent a second product IR or reintroduce V2.  
4. Prefer thin vertical slices; archive completed suites instead of leaving stale “CURRENT: commit…”.
