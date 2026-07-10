# Micro-Task: MCP session store + overview on V3

**Parent**: WP4  
**Difficulty**: Medium  
**Estimated Tokens**: ~10k  
**Status**: [ ] Not Started

## Objective

MCP **workspace/session** + overview tools as a **consumer** of the DomainModeling API (workspace is an MCP concept, not core).

## Context

- `spikes/mcp-guiding-principles.md`
- `spikes/first-v3-consumer.md`
- `v3-completion-plan.md` §1.2 (workspace in MCP only)
- WP1–WP2 complete (factory + queries)

## Exact Steps

1. Session/workspace store in Poly.Mcp: `sessionId` → V3 `Domain` + revision + last analysis (no V2 Domain). **Do not** put this type in DomainModeling.
2. Tools: CreateDomainSession, GetDomainOverview (and optional ListSessions / Interrogate).
3. Bootstrap via DomainModeling factory/builtins.
4. Descriptions per MCP principles; concise response envelope + affordances.
5. Call DomainModeling evolve/query APIs only — no domain logic in tools.
6. Tests may live under Poly.Tests and reference Poly.Mcp public session types.

## Verification

- [ ] Build Poly.Mcp
- [ ] No `Poly.Data.Modeling` in new code paths
- [ ] Tool count for this slice is small and described

## Out of Scope

- Full evolve tool set (next task); export/import; evaluate
