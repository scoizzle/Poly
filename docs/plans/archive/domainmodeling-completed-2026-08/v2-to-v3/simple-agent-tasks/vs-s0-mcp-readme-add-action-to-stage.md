# Micro-Task: Fix MCP README row for add_action_to_stage

**Suite:** [`vs-README.md`](vs-README.md) **#0.2a** *(nit — does not block Slice 1)*  
**Depends on:** **#0.2** Done  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~2k tokens  
**Status:** [x] **Done** (2026-07-12) — README: “Creates a new action on a stage”.  

## Objective

`Poly.Mcp/README.md` tool table must match the tool: **creates a new stage-local action**, not “places an existing action.”

## Required Reading

- `Poly.Mcp/README.md` — `add_action_to_stage` row  
- `Poly.Mcp/Tools/V3DomainTools.cs` — current Description  

## Exact Steps

1. Change README purpose text to match Description (create stage-local / available only in that stage).  
2. Do not change tool behavior.  

## Verification

- [ ] README and tool Description agree  
- [ ] No V2 claims reintroduced  

## Output

- `Poly.Mcp/README.md` only  
- Summary optional for nit  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes:**
