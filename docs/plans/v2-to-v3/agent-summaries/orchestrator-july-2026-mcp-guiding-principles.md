# Orchestrator Summary: MCP guiding principles from research

**Date**: 2026-07-10  
**Role**: Orchestrator  

## What we did

Researched MCP / agent-tool best practices and codified them for Poly’s M2 MCP rewrite.

## Key sources

- Anthropic — Writing effective tools for agents  
- Phil Schmid — MCP server best practices (UI for agents)  
- AWS — MCP tool design strategy (tool count balance)  
- MCP Tools specification (errors, annotations, schemas)  
- Block — workflow-first MCP playbook  

## Synthesis for Poly

- **Composition** stays on the **direct domain API**.  
- **Curation** (fewer, better tools; outcome + atomic mix; descriptions; concise responses; recoverable errors) is the **MCP** job.  
- Do **not** port ~80 V2-shaped tools 1:1.  
- Target ~10–25 tools for first M2 ship; eval-driven growth.  

## Artifacts

- `docs/plans/v2-to-v3/spikes/mcp-guiding-principles.md` (canonical)  
- Linked from master roadmap, first-v3-consumer, decision ADR, plans README  

## Next

Implement direct API + curated MCP inventory against the checklist in the principles spike.  
