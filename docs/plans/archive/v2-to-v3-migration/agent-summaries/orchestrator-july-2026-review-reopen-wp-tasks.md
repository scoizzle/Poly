# Orchestrator Summary: Reopen WP1–WP4 as In Progress after review

**Date**: 2026-07-10  
**Role**: Orchestrator  

## What

Code review of the initial WP1–WP4 implementation found follow-ups. Micro-tasks are **not Done**.

## Action

- All `wp1-*` … `wp4-*` micro-tasks set to **In Progress** with explicit follow-up checklists.
- `simple-agent-tasks/README.md`: **Continue with In Progress first** is mandatory.
- `v3-completion-plan.md` §11 + progress log updated.
- `master-roadmap.md` immediate starting point updated.

## Priority order for executors

1. `wp1-v3-builtin-catalog` (factory failure path)
2. `wp1-sever-policyevaluator-v2` (grep gate)
3. `wp2-domain-query-projections` (README Root)
4. `wp2-direct-api-happy-path-tests`
5. `wp3-evolution-rollback-suite`
6. `wp4-mcp-session-and-overview`
7. `wp4-mcp-evolve-tools` (`apply_evolution`)
8. `wp4-retire-v2-domaintools`

Do not start `ws8-*` or WP5+ until the above are Done (unless blocked on eval for the slice).
