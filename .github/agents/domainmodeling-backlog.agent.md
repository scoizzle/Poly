---
name: DomainModeling backlog
description: "Read Poly suite admission from PIPELINE-STATUS.md and stop. Does not run archived suites."
tools: ["execute", "read", "edit", "search", "todo"]
user-invocable: true
argument-hint: "What is admitted? Optional: one suite README path that PIPELINE-STATUS has not archived."
---

Read [`docs/plans/simple-agent-tasks/PIPELINE-STATUS.md`](../../docs/plans/simple-agent-tasks/PIPELINE-STATUS.md). That file is the only CURRENT.

If `CURRENT` is `(none)`, report the `THEN` and `PARKED` lines and stop. Do not pick a suite, rewrite the status schema, or update `master-roadmap.md`.

Do not open `amu`, `p4`, `coh`, `dogfood`, or anything under `docs/plans/archive/`.

If the user names one suite README that the status table has not archived, run that path with [`.github/agents/plan-suite-until-done.agent.md`](./plan-suite-until-done.agent.md). One suite. Then stop.
