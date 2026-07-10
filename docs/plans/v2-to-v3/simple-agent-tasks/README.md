# Simple Agent Tasks (Micro-Tasks for Smaller Models)

**Purpose**: Small, self-contained tasks for lower-context / cheaper agents.  
**Last Updated**: 2026-07-10

## Current Focus (July 2026)

**Completion plan:** [`../v3-completion-plan.md`](../v3-completion-plan.md) — gaps G1–G17 + work packages WP1–WP9.  
**Consumer:** MCP + direct API (`../spikes/first-v3-consumer.md`).  
**MCP rules:** `../spikes/mcp-guiding-principles.md`.

| Priority | Package | Pick these |
|----------|---------|------------|
| **1** | **WP1** | `wp1-v3-builtin-catalog.md`, `wp1-sever-policyevaluator-v2.md` |
| **2** | **WP2** | `wp2-domain-query-projections.md`, `wp2-direct-api-happy-path-tests.md` |
| **3** | **WP3** | `wp3-evolution-rollback-suite.md`, `ws8-e2e-policy-vm-eval.md` |
| **4** | **WP4** | `wp4-mcp-session-and-overview.md`, `wp4-mcp-evolve-tools.md`, `wp4-retire-v2-domaintools.md` |
| **5** | **WP5** | `ws8-domainexpression-lower-smoke-matrix.md` (if eval tools ship) |
| **6** | Polish | `ws4-agent-trace-reading-guide.md` |
| **Skip** | Old foundation | All `ws1-*`, `ws2-research-*`, `ws3-add-*` — **superseded** |

**Quality bar:** correctness · composition on direct API · curated MCP · tests · natural code.

## Philosophy

- One task = one small, verifiable change.
- Minimal context (ideally &lt; 4k–8k tokens).
- Clear verification (build + named tests).
- Link only the needed decisions / source files.
- Prefer **implementation** over design.
- New DomainChange / tools only with a consumer call site + test.

## How to Use

1. **Orchestrators**: Decompose WPs into micro-tasks here; keep status accurate; update `v3-completion-plan.md` progress log when a WP finishes.
2. **Executors**: Pick **Not Started** tasks in WP order. Follow steps strictly. File `agent-summaries/`.
3. **Do not** claim superseded WS1 skeleton tasks.

## Task Format

Use `TEMPLATE-micro-task.md`.

## Status Legend for Older Tasks

| Pattern | Status |
|---------|--------|
| `ws1-*` (evolution skeleton era) | **Superseded** |
| `ws2-research-nodeid-*` | **Superseded** |
| `ws3-add-*` / `ws3-confirm-*` | **Superseded** |
| `ws3-name-first-v3-consumer` | **Done** |
| `ws6-audit-*` | Optional hygiene |
| `ws8-*` | Active when WP3/WP5 pulls |
| `wp1-*` … `wp4-*` | **Active execution path** |

## Related

- Completion plan: `../v3-completion-plan.md`
- Parent roadmap: `../master-roadmap.md`
- Orchestration: `../orchestration-guide.md`
- Decisions: `docs/decisions/2026-v2-to-v3-domain-modeling-port.md`
