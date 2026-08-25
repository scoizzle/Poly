---
name: plan-suite-until-done
description: >
  Execute a Poly docs/plans simple-agent-tasks suite until complete (orient →
  implement → verify → record). Use when the user asks to run a suite until done,
  finish amu/p4/coh/dogfood tasks, or process a *-README.md under simple-agent-tasks.
  For the full multi-suite pipeline, prefer the domainmodeling-backlog custom agent.
---

# Plan suite until done (Copilot skill)

## When to use

- User says “run amu until done”, “finish the p4 suite”, “execute coh”, “dogfood until-done”
- User points at `docs/plans/simple-agent-tasks/*-README.md` or dogfood-README

## Instructions

Follow the agent profile **`.github/agents/plan-suite-until-done.agent.md`** end-to-end.

1. Resolve suite key/path (`amu` / `p4` / `coh` / `dogfood` / full path).  
2. Loop first `[ ]` task → implement → `dotnet build` + tests → mark `[x]`.  
3. Respect file ownership and dogfood discovery rules (MCP-only, no product fixes).  
4. Close suite gate + pr1 before claiming Done.  
5. Do not start a second suite unless the user asked for **domainmodeling-backlog**.

## Full pipeline

If the user wants **all** suites: use agent **`domainmodeling-backlog`** and  
`docs/plans/simple-agent-tasks/SUITE-OF-SUITES.md`.
