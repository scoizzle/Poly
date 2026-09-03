# Archived: completed / superseded plans (late 2026-08)

**Archived:** 2026-09-01  
**Reason:** Live `docs/plans/` still held finished August suites and executed design docs after the mid-August archive pass.  
**Do not execute** these as CURRENT work.

## Current path (live)

| Need | Location |
|------|----------|
| Admission + CURRENT | [`../../README.md`](../../README.md) |
| Ready agent suites | [`../../simple-agent-tasks/READY-TO-TASK.md`](../../simple-agent-tasks/READY-TO-TASK.md) |
| Master roadmap | [`../../v2-to-v3/master-roadmap.md`](../../v2-to-v3/master-roadmap.md) |
| Pre-ship gate (still live) | [`../../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md) |
| Product DSL guide | `Poly.Mcp/Docs/poly-dsl-guide.md` |
| Mechanisms | [`../../../CORE.md`](../../../CORE.md) |
| Live probe fixtures | [`../../../probes/`](../../../probes/) |

## What is here

| Area | Contents |
|------|----------|
| **Root** | grammar-revision, dead-dual, vision-cleanup, relationship synthesis, for-invoke, minify parent, executed 2026-08 design docs |
| **`simple-agent-tasks/`** | Completed suites: **gpure**, **mcp-minify**, **ile**, **pack-1**, **rewrite-to-master** |
| **`v2-to-v3/`** | Dogfood agent summaries, VS-S1 summaries, stale qe-pointing README, first-v3-consumer spike |

## Suites (all complete)

| Suite | Theme |
|-------|--------|
| gpure | Pure Grammar product path |
| mcp-minify | Catalog 46→24; DSL-only expressions |
| interpretation-language-engine | Language VM contract; `Compile` fail-closed |
| pack-1 | TokenWriter + binders + printer |
| rewrite-to-master | Product trunk is `master` (PR 26) |
| grammar-revision | v2 engine + DSL cutover |
| dead-dual | Validation + Text.Matching deleted |
| vision-cleanup | Slices 1–3 (one door, session.Analyze) |

## Rules

Do not reopen from this folder. Unpark only by admitting a **new** suite against `PIPELINE-STATUS.md`.
