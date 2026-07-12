# Plans & Roadmaps

Execution-oriented planning — roadmaps, phase breakdowns, task trackers.

**Not plans:** platform mechanisms and “use this, not that” → **[`docs/CORE.md`](../CORE.md)**.  
Architectural rationale → **`docs/decisions/`**. Module maps → **`Poly/*/README.md`**.

## Active plans

| Plan | Role |
|------|------|
| [**V3 finish — vertical slices**](v2-to-v3/vertical-slice-finish-plan.md) | Remaining product work as slices 0–3 |
| [**Simple-agent queue (`vs-*`)**](v2-to-v3/simple-agent-tasks/vs-README.md) | **Pick order for smaller agents** — one micro-task file at a time |
| [**Post–system-review correctness hardening**](2026-07-11-review-fix-plan.md) | Trust layer 1 detail (feeds **Slice 0**); WP-A–J |
| [**V2 → V3 Master Roadmap**](v2-to-v3/master-roadmap.md) | Milestones M1–M4 (delete **complete**); M2 product close via vertical slices |
| [**V3 Completion Plan**](v2-to-v3/v3-completion-plan.md) | Historical WP1–WP9 inventory; execution order superseded by vertical-slice plan for remaining work |
| [First V3 consumer spike](v2-to-v3/spikes/first-v3-consumer.md) | Named M2 consumer + happy path |
| [MCP guiding principles](v2-to-v3/spikes/mcp-guiding-principles.md) | Agent-tool design for V3 MCP |
| [V2 → V3 Workstreams](v2-to-v3/workstreams/) | Workstream detail (WS8 pull for policy/eval) |
| [V2 → V3 Simple Agent Tasks](v2-to-v3/simple-agent-tasks/) | Micro-tasks — **[WS8 Phase B A+](v2-to-v3/simple-agent-tasks/ws8-README.md)** (#6d–#6h invariants, then #7–#11); WP7/WP8 superseded |
| [Orchestration Guide](v2-to-v3/orchestration-guide.md) | Multi-agent operating model |

## Deferred (do not execute yet)

| Plan | Role |
|------|------|
| [**Post–V2-delete naming cleanup**](post-v2-delete-naming-cleanup.md) | Drop product `V3*` / “V3 stack” labels now that V2 is gone — MCP types, demos, `UseV3*`, active prose. **After** M2 / between idle slices; not the daily `vs-*` queue |
| [**`Poly.Ast` + `Poly.Analysis` module split**](poly-ast-analysis-module-split.md) | Rename/split today’s `Poly.Syntax` into IR (`Ast`) + analysis framework (`Analysis`). **Blocked on** DomainModeling product stability and clean working tree. Nodes stay **out of** Interpretation. |

## Interpretation (current)

Interpretation is **not** driven by a large open task list anymore.

| Source of truth | Content |
|-----------------|--------|
| `Poly/Interpretation/README.md` | Pipeline, modules, pass order |
| `docs/decisions/2026-06-08-vm-as-canonical-semantics.md` | VM sole engine |
| `docs/decisions/2026-07-04-primitives-as-canonical-ir.md` | Historical title; body = direct AST→ABI |
| `docs/decisions/2026-06-08-domain-lowering-boundary.md` | Domain → generic AST only |

Optional still-valid plan snippets (verify against code before implementing):

- [array-specialization-plan.md](array-specialization-plan.md) — emitter TypeIs elimination
- [analyzer-improvements.md](analyzer-improvements.md) — analysis quality ideas
- [future-platform-capabilities.md](future-platform-capabilities.md) — deferred product ideas
- [neurosymbolic-platform-from-first-principles.md](neurosymbolic-platform-from-first-principles.md) — vision sketch
- Anti-patterns [001](anti-pattern-001-duplicate-tree-walks.md), [003](anti-pattern-003-extension-point-accretion.md), [004](anti-pattern-004-interface-new-hiding.md), [005](anti-pattern-005-second-system-effect.md), [007](anti-pattern-007-single-point-dependency.md)

Holistic architecture notes (living review, not a task list):

- [Interpretation System Architecture Review](../interpretation-system-architecture-review.md) — **partially stale**; prefer module README + decisions for the pipeline. Do not treat its companion trackers as live (they are archived).

## Archived (do not execute)

| Archive | Contents |
|---------|----------|
| [**archive/interpretation/**](archive/interpretation/README.md) | Plans for µop/bytecode/tree-walker/primitive IR, completed direct-lowering campaigns, and the INT/ANA resolution trackers |

Agents **must not** implement work from the archive without an explicit re-open validated against `DirectVmAbiEmitter`.

## Legacy redirects

- [v2-to-v3-domain-modeling-port-roadmap.md](v2-to-v3-domain-modeling-port-roadmap.md) → points at `v2-to-v3/master-roadmap.md`

## Guidelines

1. Consult `docs/decisions/` before significant work.
2. Prefer **`v3-completion-plan.md`** for “what next” (WP order); master roadmap for milestones.
3. When a design choice lands, update or add a decision record — not a new competing IR plan.
4. New Interpretation work needs a **first consumer** (direct domain API / MCP / tests), per core engineering principles.
