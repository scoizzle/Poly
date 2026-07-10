# Simple Agent Tasks (Micro-Tasks for Smaller Models)

**Purpose**: Small, self-contained tasks for lower-context / cheaper agents.  
**Last Updated**: 2026-07-10

## Current Focus (July 2026)

| Priority | Parent | What to pick |
|----------|--------|----------------|
| **1 — Active** | **WS8** | DomainExpression → VM e2e, contract interface gen, lowering consumer gaps |
| **2 — Polish** | **WS4** | Trace / diagnostic quality for agents |
| **3 — Hygiene** | **WS6** | Docs only (usually orchestrator) |
| **Skip** | WS1 / WS2 / WS3 micro-tasks | Marked **Done** or **Superseded** — evolution foundation already shipped |

Interpretation / VM micro-optimizations are **out of scope** for this directory unless a WS8 task explicitly requires a VM fix.

## Philosophy

- One task = one small, verifiable change.
- Minimal context (ideally &lt; 4k–8k tokens).
- Clear verification (build + named tests).
- Link only the needed decisions / source files.
- Prefer **implementation** over design.

## How to Use

1. **Orchestrators**: Decompose WS8/WS4 into micro-tasks here; keep status accurate.
2. **Executors**: Pick **Not Started** tasks for WS8/WS4 only. Follow steps strictly. File `agent-summaries/`.
3. **Do not** claim superseded WS1 “skeleton” tasks — the applicator already exists.

## Task Format

Use `TEMPLATE-micro-task.md`. Every task must include:

- Parent workstream (WSx)
- Difficulty + rough token budget
- Exact steps + verification
- Status: Not Started / In Progress / Done / Superseded

## Status Legend for Older Tasks

Many files under this directory were written for **greenfield WS1**. Treat them as follows:

| Pattern | Status |
|---------|--------|
| `ws1-implement-basic-domain-evolution-skeleton` | **Superseded** — `DomainEvolution` is real |
| `ws1-define-evolution-result-record` | **Superseded** — records exist |
| `ws1-implement-evolution-trace-record` | **Superseded** |
| `ws1-implement-minimal-applicator-skeleton` | **Superseded** — applicator works |
| `ws1-implement-minimal-noop-change-handler` | **Superseded** |
| `ws1-define-first-domainchange-types` | **Superseded** — 66 subtypes |
| `ws1-add-nodeid-preservation-test` | **Done** if tests exist; else verify once then Done |
| `ws1-improve-trace-affected-nodes` | Prefer WS4 tasks |
| `ws1-sketch/propose-fluent-evolution` | **Superseded** — `EvolutionBuilder` is large and fluent |
| `ws2-research-nodeid-behavior` | **Superseded** — mechanical continuity in applicator |
| `ws3-*` | **Superseded** — ops landed in DomainChange + EvolutionBuilder |
| `ws6-audit-workstream-against-principles` | Optional hygiene |

Active tasks live at the top of the directory listing by date or are named `ws8-*` / `ws4-*`.

## Related

- Parent roadmap: `../master-roadmap.md`
- Orchestration: `../orchestration-guide.md`
- Decisions: `docs/decisions/2026-v2-to-v3-domain-modeling-port.md`
