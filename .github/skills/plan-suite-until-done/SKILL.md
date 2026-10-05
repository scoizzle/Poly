---
name: plan-suite-until-done
description: >
  Execute one Poly suite README that PIPELINE-STATUS has not archived.
  Use when the user gives a suite path and asks to run it until done or run
  the next task. Refuses archived suites.
---

# Plan suite until done

Follow [`.github/agents/plan-suite-until-done.agent.md`](../../agents/plan-suite-until-done.agent.md).

The user must name a `*-README.md` path. Read `docs/plans/simple-agent-tasks/PIPELINE-STATUS.md` first. If the suite is archived, stop. One suite per run.
