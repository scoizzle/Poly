# Micro-Task: MCP session store + overview on V3

**Parent**: WP4  
**Difficulty**: Medium  
**Estimated Tokens**: ~10k  
**Status**: [ ] Not Started

## Objective

New (or rewritten) MCP session + overview tools using **only** V3 direct API / DomainModeling.

## Context

- `spikes/mcp-guiding-principles.md`
- `spikes/first-v3-consumer.md`
- WP1–WP2 complete (factory + queries)

## Exact Steps

1. Session store: `sessionId` → V3 `Domain` + revision + last analysis (no V2 Domain).
2. Tools: CreateDomainSession, GetDomainOverview (and optional ListSessions / Interrogate).
3. Bootstrap with V3 builtins.
4. Descriptions per MCP principles; concise response envelope + affordances.
5. Call DomainModeling queries — no domain logic in tools.
6. Smoke test: invoke tool methods from a test project if feasible.

## Verification

- [ ] Build Poly.Mcp
- [ ] No `Poly.Data.Modeling` in new code paths
- [ ] Tool count for this slice is small and described

## Out of Scope

- Full evolve tool set (next task); export/import; evaluate
