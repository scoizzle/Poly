# Plans & Roadmaps

Execution-oriented planning — roadmaps, phase breakdowns, task trackers.

Architectural rationale lives in **`docs/decisions/`**. Module maps live in **`Poly/*/README.md`**.

## Active plans

| Plan | Role |
|------|------|
| [**V2 → V3 Master Roadmap**](v2-to-v3/master-roadmap.md) | Milestones M1–M4, quality bar, readiness checklist |
| [**V3 Completion Plan**](v2-to-v3/v3-completion-plan.md) | **Day-to-day execution:** gaps G1–G17, work packages WP1–WP9, acceptance criteria |
| [First V3 consumer spike](v2-to-v3/spikes/first-v3-consumer.md) | Named M2 consumer + happy path + out of scope |
| [MCP guiding principles](v2-to-v3/spikes/mcp-guiding-principles.md) | Agent-tool research + Poly constraints for MCP rewrite |
| [V2 → V3 Workstreams](v2-to-v3/workstreams/) | Workstream detail (`ws8-*` pull-only; WS1 foundation complete) |
| [V2 → V3 Simple Agent Tasks](v2-to-v3/simple-agent-tasks/) | Micro-tasks — **`wp1-*`…`wp4-*` first**, then `ws8-*` / `ws4-*` if pulled |
| [Orchestration Guide](v2-to-v3/orchestration-guide.md) | Multi-agent operating model |

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
