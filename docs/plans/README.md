# Plans & Roadmaps

Execution-oriented planning — roadmaps, phase breakdowns, task trackers.

**Not plans:** platform mechanisms → **[`docs/CORE.md`](../CORE.md)**.  
Architectural rationale → **`docs/decisions/`**. Module maps → **`Poly/*/README.md`**.

---

## Active (use these)

| Plan | Role |
|------|------|
| [**MCP Phase 3 + RT + SA**](v2-to-v3/mcp-phase3-oracle-surface.md) | Phase 3 + RT + SA MVP **done**; residuals pull |
| [**Effect surface completeness**](v2-to-v3/effect-surface-completeness.md) | Usefulness track — IR/DSL/MCP effect parity (delete, link, invoke, …) |
| [**DSL query surface**](v2-to-v3/dsl-query-surface.md) | Q1′ authoring + DMREL001 + Q1''''' hygiene; **§14 Rel exists bug** next; RT eval **pull** |
| [**MCP dogfood orchestrator**](v2-to-v3/mcp-dogfood-orchestrator.md) | Dogfood — [report 1](v2-to-v3/agent-summaries/dogfood/DOGFOOD-REPORT-20260718.md) · [report 2](v2-to-v3/agent-summaries/dogfood/DOGFOOD-REPORT-2-20260718.md) |
| [**Simple-agent queue (`qe-*`)**](v2-to-v3/simple-agent-tasks/qe-README.md) | **Primary pick** — §14 Q1''''''.1 then Q3′ pull |
| [**Simple-agent queue (`vs-*`)**](v2-to-v3/simple-agent-tasks/vs-README.md) | Historical M2 vertical-slice tasks — **done**; do not reopen |
| [**MCP tool-surface expansion**](v2-to-v3/mcp-tool-surface-expansion.md) | Backlog §0 — RT next; pull-only after |
| [**Vertical-slice finish plan**](v2-to-v3/vertical-slice-finish-plan.md) | Product path status; M2 **Done**; post-M2 calendar |
| [**Post–V2-delete naming cleanup**](post-v2-delete-naming-cleanup.md) | Drop product `V3*` labels (MCP types, demos, `UseV3*`) — idle green tree |
| [**Post–system-review correctness hardening**](2026-07-11-review-fix-plan.md) | Honesty/VM residuals (mostly Done; optional 0.1d, WP-E/F later) |
| [**Master roadmap (milestones)**](v2-to-v3/master-roadmap.md) | M1–M4 done; **What next** = Q1′′′ residuals → Q3′ pull |
| [MCP guiding principles](v2-to-v3/spikes/mcp-guiding-principles.md) | Agent-tool design (still valid) |
| [First V3 consumer spike](v2-to-v3/spikes/first-v3-consumer.md) | Named consumer decision (historical + quality bar) |

---

## Deferred (do not execute yet)

| Plan | Role |
|------|------|
| [**Domain plugin / extension platform**](domain-plugin-extension-platform.md) | Pointer → experiment: future DSL+facet+lowering packs — **research only** |
| [**`Poly.Ast` + `Poly.Analysis` module split**](poly-ast-analysis-module-split.md) | Split `Poly.Syntax` into IR + analysis framework — after product stability |
| [array-specialization-plan.md](array-specialization-plan.md) | Optional emitter TypeIs elimination — verify against `DirectVmAbiEmitter` before claiming |
| [analyzer-improvements.md](analyzer-improvements.md) | Optional analysis quality ideas — not a product gate |
| [future-platform-capabilities.md](future-platform-capabilities.md) | Idea backlog — not an execution queue |

---

## Guardrails (not task lists)

Anti-patterns (still valid): [001](anti-pattern-001-duplicate-tree-walks.md), [003](anti-pattern-003-extension-point-accretion.md), [004](anti-pattern-004-interface-new-hiding.md), [005](anti-pattern-005-second-system-effect.md), [007](anti-pattern-007-single-point-dependency.md).

---

## Interpretation

| Source of truth | Content |
|-----------------|--------|
| `Poly/Interpretation/README.md` | Pipeline, modules, pass order |
| `docs/decisions/2026-06-08-vm-as-canonical-semantics.md` | VM sole engine |
| `docs/decisions/2026-07-04-primitives-as-canonical-ir.md` | Historical title; body = **direct AST→ABI** |
| `docs/decisions/2026-06-08-domain-lowering-boundary.md` | Domain → generic AST only |

**No open Interpretation mega-plan.** Do not resurrect µop / bytecode product IR.

---

## Archived (do not execute)

| Archive | Contents |
|---------|----------|
| [**archive/interpretation/**](archive/interpretation/README.md) | µop/bytecode/tree-walker/primitive IR era |
| [**archive/v2-to-v3-migration/**](archive/v2-to-v3-migration/README.md) | Completed migration: WS8 µop docs, WP/ws micro-tasks, workstreams, completion inventory |
| [**archive/vision-historical/**](archive/vision-historical/README.md) | Vision sketches that contradict shipped execution model |

Agents **must not** implement archive work without an explicit re-open validated against `docs/CORE.md` and current code.

---

## Guidelines

1. **What next?** → [`v2-to-v3/master-roadmap.md`](v2-to-v3/master-roadmap.md) agent pick · [`dsl-query-surface.md`](v2-to-v3/dsl-query-surface.md) **§14** · [`qe-README.md`](v2-to-v3/simple-agent-tasks/qe-README.md).  
2. Consult `docs/decisions/` and `CORE.md` before significant work.  
3. Do not invent a second product IR or reintroduce V2.  
4. Prefer thin vertical slices over completing archived frameworks.
