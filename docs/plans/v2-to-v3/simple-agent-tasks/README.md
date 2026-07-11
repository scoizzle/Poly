# Simple Agent Tasks (Micro-Tasks for Smaller Models)

**Purpose**: Small, self-contained tasks for lower-context / cheaper agents.  
**Last Updated**: 2026-07-10

## Operating rule (mandatory)

**Continue with In Progress tasks first.**  
Do **not** start new Not Started work (WP5+, breadth flush, Actor, etc.) while any `wp1-*`…`wp4-*` task is **In Progress**. Finish code-review follow-ups on those tasks, mark them **Done**, then proceed.

Status legend:

| Mark | Meaning |
|------|---------|
| `[ ] Not Started` | No implementation claim |
| `[~] In Progress` | Implementation started or partially shipped; **follow-ups required** |
| `[x] Done` | Acceptance + follow-ups complete |

---

## Current Focus (July 2026)

**Completion plan:** [`../v3-completion-plan.md`](../v3-completion-plan.md)  
**Consumer:** MCP + direct API (`../spikes/first-v3-consumer.md`).  
**MCP rules:** `../spikes/mcp-guiding-principles.md`.

### In Progress first (code-review reopen — 2026-07-10)

Another model shipped an initial WP1–WP4 slice. Review found follow-ups; tasks are **not Done**.

| Priority | Task | Focus of follow-ups |
|----------|------|---------------------|
| **1** | `wp1-v3-builtin-catalog.md` | Bootstrap-then-configure; false-positive failure test; dead ternary |
| **2** | `wp1-sever-policyevaluator-v2.md` | Re-confirm DomainModeling V2 grep gate |
| **3** | `wp2-domain-query-projections.md` | README `result.Root` |
| **4** | `wp2-direct-api-happy-path-tests.md` | Real failure / no-op coverage |
| **5** | `wp3-evolution-rollback-suite.md` | Optional no-op hardening; then Done |
| **6** | `wp4-mcp-session-and-overview.md` | Structured payloads, affordances, diagnostics, smoke tests |
| **7** | `wp4-mcp-evolve-tools.md` | Remove/redesign `apply_evolution`; smoke path; honest success |
| **8** | `wp4-retire-v2-domaintools.md` | Re-verify cliff + deprecation note |

### After In Progress are Done

| Priority | Package | Pick these |
|----------|---------|------------|
| Next | **WP3/WP5** | `ws8-e2e-policy-vm-eval.md` if slice needs eval |
| Later | **WP5** | `ws8-domainexpression-lower-smoke-matrix.md` |
| Polish | **WS4** | `ws4-agent-trace-reading-guide.md` |
| **Skip** | Old foundation | All `ws1-*`, `ws2-research-*`, `ws3-add-*` — **superseded** |

**Quality bar:** correctness · composition on direct API · curated MCP · tests · natural code.

## Philosophy

- One task = one small, verifiable change.
- Minimal context (ideally &lt; 4k–8k tokens).
- Clear verification (build + named tests).
- Link only the needed decisions / source files.
- Prefer **implementation** over design.
- New DomainChange / tools only with a consumer call site + test.
- **In Progress + follow-ups beat greenfield breadth.**

## How to Use

1. **Orchestrators**: Keep status accurate; when review finds gaps, set **In Progress** and list follow-ups on the task file (do not silently mark Done).
2. **Executors**: Pick **In Progress** tasks first (table above). Complete follow-ups → mark **Done**. Only then take Not Started.
3. **Do not** claim superseded WS1 skeleton tasks.
4. File `agent-summaries/` when closing a task.

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
| `ws8-*` | Active only after WP1–WP4 In Progress closed (unless blocked) |
| `wp1-*` … `wp4-*` | **In Progress** — finish follow-ups first |

## Related

- Completion plan: `../v3-completion-plan.md`
- Parent roadmap: `../master-roadmap.md`
- Orchestration: `../orchestration-guide.md`
- Decisions: `docs/decisions/2026-v2-to-v3-domain-modeling-port.md`
