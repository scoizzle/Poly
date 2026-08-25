# Archived: V2 → V3 migration plans

**Archived:** 2026-07-12  
**Reason:** Migration **complete** (V2 deleted; M2 first-consumer vertical slice product-complete). Remaining files here **contradict or obsolete** the current path if executed as live work.

## Current path (do this instead)

| Need | Open |
|------|------|
| Agent pick queue | [`../../v2-to-v3/simple-agent-tasks/vs-README.md`](../../v2-to-v3/simple-agent-tasks/vs-README.md) |
| Product slice status | [`../../v2-to-v3/vertical-slice-finish-plan.md`](../../v2-to-v3/vertical-slice-finish-plan.md) |
| Milestones | [`../../v2-to-v3/master-roadmap.md`](../../v2-to-v3/master-roadmap.md) |
| Naming cleanup (drop V3*) | [`../../post-v2-delete-naming-cleanup.md`](../../post-v2-delete-naming-cleanup.md) |
| Platform mechanisms | [`../../../CORE.md`](../../../CORE.md) |

## Why these were archived

| Category | Contradiction / obsolescence |
|----------|------------------------------|
| **`workstreams/ws8-*`** | Still documents product pipeline as **Syntax → µops → Assemble → ProgramCompiler** — superseded by **direct AST → `DirectVmAbiEmitter`** |
| **`workstreams/ws1–ws7`** | Phase 1 complete or marked superseded; not claimable |
| **`v3-domain-lowering-pass-design.md`** | Speculative full-domain lowering framework (`V3*LoweringPass` catalog) not matching shipped `DomainExpressionLoweringPass` thin path |
| **`v3-completion-plan.md` / WP/ws micro-tasks** | Execution order superseded by vertical slices; many tasks Done/Superseded; agents were still pointed at WS8 Phase B |
| **`00-bootstrap` / orchestration-guide** | Historical ignition; re-entry text pointed at archived WS8 |
| **wp\*/ws\* simple-agent-tasks** | Superseded by **`vs-*`** suite |
| **orchestrator / ws5 agent-summaries** | Provenance only |

## Layout

```text
archive/v2-to-v3-migration/
  designs/           # completion plan, bootstrap, orchestration, lowering design
  workstreams/       # ws1–ws8
  simple-agent-tasks/# wp*, ws* micro-tasks
  agent-summaries/   # pre-vs orchestrator/ws5 summaries
  spikes/            # completed design spikes
  v2-to-v3-domain-modeling-port-roadmap.md  # old redirect stub
```

## Rules

- **Do not execute** archive tasks without an explicit re-open validated against `docs/CORE.md` and current code.
- Historical reading for “why we chose X” is fine; do not treat Status: Active headers inside as current.

Also archived (vision): [`../vision-historical/neurosymbolic-platform-from-first-principles.md`](../vision-historical/neurosymbolic-platform-from-first-principles.md) — µop-sequence product loop sketch, not the shipped execution model.
