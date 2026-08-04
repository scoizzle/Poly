# Plans & Roadmaps

Execution-oriented planning — roadmaps, phase breakdowns, task trackers.

**Not plans:** platform mechanisms → **[`docs/CORE.md`](../CORE.md)**.  
Architectural rationale → **`docs/decisions/`**. Module maps → **`Poly/*/README.md`**.

---

## Admission control (2026-08-04)

**One primary implementation workstream at a time.** Everything else is Done, Parked, or Pull — not “also open.”

| Rule | Meaning |
|------|---------|
| **CURRENT** | Only what master-roadmap Agent pick says (or `(none)`). |
| **Park before open** | Finish or park the live suite before admitting the next. |
| **Proposals ≠ queues** | Research docs (e.g. DSL absorption) stay parked until one P* is admitted as a suite. |
| **Pull ≠ CURRENT** | Dogfood, Q4, grammar, packs — available when admitted, not parallel debt. |

**Agent pick source of truth:** [`v2-to-v3/master-roadmap.md`](v2-to-v3/master-roadmap.md) → “Agent pick (one line)”.

---

## Active (index + reference only)

| Plan | Role |
|------|------|
| [**Master roadmap**](v2-to-v3/master-roadmap.md) | Milestone index + **one-line agent pick** (CURRENT) |
| [**Capability inventory**](../domainmodeling-capability-inventory.md) | What ships (reference, not a queue) |
| [**`pr1` review gate**](v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md) | Always-on pre-ship process |

---

## Parked (do not execute until unparked)

| Plan | Role | Unpark when |
|------|------|-------------|
| [**DSL absorption proposals**](domain-dsl-absorption-proposals.md) | Experiment → product styles (P1 temporal **lower priority**, multi-hop, when any/all, …) | Admit **one** P* as sole suite; **not** dates-first by default |
| [**MCP dogfood protocol**](v2-to-v3/mcp-dogfood-protocol.md) · [`dogfood-*`](v2-to-v3/simple-agent-tasks/dogfood-README.md) · [`dogfood-fix-*`](v2-to-v3/simple-agent-tasks/dogfood-fix-README.md) | Scenario discovery; fixes mostly done | Admit dogfood (e.g. SPE surface) as primary |
| [**MCP dogfood orchestrator**](v2-to-v3/mcp-dogfood-orchestrator.md) | Dogfood loop tooling | With dogfood admission |
| [**Domain analysis unification**](domain-analysis-unification.md) · [`dau-*`](simple-agent-tasks/dau-README.md) | Product bar met; codegen track paused | Explicit DAU reopen |
| [**Grammar framework integration**](grammar-integration.md) | Draft — not a prerequisite for temporal/SPE | Product stability + explicit pick |
| [**Analysis-consuming lowering**](analysis-consuming-lowering.md) | Draft | Explicit pick |
| [**DomainAuthoringContext removal**](domain-authoring-context-removal-plan.md) | Draft | Explicit pick |
| [**Post–V2-delete naming cleanup**](post-v2-delete-naming-cleanup.md) | Drop product `V3*` labels | Idle green tree + explicit pick |
| [**Platform velocity review**](platform-velocity-review.md) | Pain inventory (2026-07-25) | Pull items only when admitted |
| [**Effect surface completeness**](v2-to-v3/effect-surface-completeness.md) | Kernel shipped; E5 / E6.1 pull | Dogfood or explicit effect suite |
| [**MCP tool-surface expansion**](v2-to-v3/mcp-tool-surface-expansion.md) | Backlog §0 | After dogfood admission |
| [**Post–system-review correctness hardening**](2026-07-11-review-fix-plan.md) | Mostly done; optional residuals | Explicit residual pick |

---

## Complete (do not reopen)

| Plan | Role |
|------|------|
| [**Domain surface extensions (SPE)**](domain-surface-extensions-plan.md) · [`spe-*`](simple-agent-tasks/spe-README.md) | Export peer · entity-level when · owned policies |
| [**DSL query surface**](v2-to-v3/dsl-query-surface.md) · [`qe-*`](v2-to-v3/simple-agent-tasks/qe-README.md) | Q1′+Q3′+`link_instances`; design reference |
| [**Analysis pipeline merge**](analysis-pipeline-merge.md) · [`apm-*`](simple-agent-tasks/apm-README.md) | Registration predecessor to DAU |
| [**Infrastructure pass — NEXT**](infrastructure-pass-NEXT.md) | Under bar; archive [infra](archive/infrastructure-pass/README.md) |
| [**`vs-*` (vertical slice)**](v2-to-v3/simple-agent-tasks/vs-README.md) | Historical M2 |

Shipped recently (no open suite): DAS catalog · peer `when … as` · store-aware `Rel exists` — see master-roadmap DONE line.

---

## Deferred (do not execute yet)

| Plan | Role |
|------|------|
| [**Domain plugin / multi-DBMS packs**](domain-plugin-extension-platform.md) | → [`dsl-plugin-pipeline-experiment.md`](dsl-plugin-pipeline-experiment.md) — not current pick |
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
