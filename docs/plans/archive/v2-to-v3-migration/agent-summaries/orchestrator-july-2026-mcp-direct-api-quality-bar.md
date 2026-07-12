# Orchestrator Summary: MCP + direct API named; quality bar codified

**Date**: 2026-07-10  
**Role**: Orchestrator  

## Decision

First V3 consumer is **not** an open A/B/C choice anymore:

- **Direct domain API** = contract into DomainModeling / Syntax / VM (primary correctness surface, tests attach here).
- **MCP** = thin adapter (sessions, DTOs, tool metadata).

## Quality bar (plan-level)

1. System correctness  
2. Robustness via composition  
3. MCP + direct API as guiding light  
4. Tests (especially on the direct API)  
5. Naturally readable code  

## Plan updates

- `spikes/first-v3-consumer.md` — full decision + happy path + out of scope  
- `master-roadmap.md` — quality bar, M2 wording, Phase 2/3, checklist  
- `docs/decisions/2026-v2-to-v3-domain-modeling-port.md` — goals + status  
- `simple-agent-tasks/README.md`, `ws3-name-first-v3-consumer.md` (Done)  
- `workstreams/ws8-analysis-unification-and-lowering.md` — consumer named  

## Next

1. Implement composable direct API + TUnit for spike happy path.  
2. Wire thin MCP over it (rewrite `DomainTools` path off V2).  
3. Pull WS8 e2e policy/VM only if a tool needs runtime eval.  
4. Freeze then delete V2.  
