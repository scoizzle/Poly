# Orchestrator Summary: V3 completion plan (full gap fill)

**Date**: 2026-07-10  
**Role**: Orchestrator  

## What shipped (docs)

- **`docs/plans/v2-to-v3/v3-completion-plan.md`** — canonical implementation plan:
  - Code reality (what is solid vs missing)
  - Gaps **G1–G17** prioritized for M2
  - Work packages **WP0–WP9** with acceptance criteria
  - Direct API sketch, MCP tool budget, layout, risks
  - Micro-task index
- **WP micro-tasks**: `wp1-*`, `wp2-*`, `wp3-*`, `wp4-*` under `simple-agent-tasks/`
- Wired: master roadmap, plans README, decision ADR, WS7/WS8 stale notes, simple-agent-tasks README

## Key audit findings

- Evolution + ~66 DomainChanges + fluent EvolutionBuilder: **real**
- DE → Syntax → VM tests: **exist** (WS7 “no lowering” is stale)
- Blockers for M2: no V3 builtins (catalog still V2), PolicyEvaluator V2 using, no query façade, thin tests, MCP still V2-shaped (~80 tools)
- Not M2 blockers: Actor, full Rule system, contract gen, visual, full action simulation

## Execute next

```
WP1 builtins + sever V2 from DomainModeling
→ WP2 queries + happy-path tests
→ WP3 rollback + policy VM e2e
→ WP4 curated MCP + retire V2 DomainTools path
→ freeze → demos → delete V2
```

## Do not

- Port DomainTools 1:1
- Start Actor/contract gen before M2 unless blocked by a real consumer scenario
