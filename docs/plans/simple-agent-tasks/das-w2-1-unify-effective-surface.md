# DAS W2.1 — Unify effective action/policy surface

**Wave:** W2 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) §3.3, W2  
**Difficulty:** Medium  
**Status:** `[ ]`  
**Prereq:** W1 gate  

## Objective

One algorithm answers “effective policies/actions at stage.” MCP, capability views, and helpers agree. BehaviorPass becomes a thin pack DTO adapter or is removed.

## Tasks

- [ ] W2.1.1 Choose canonical surface (Capability recommended) and document composition rules (entity + stage; not all action policies unless product says so).
- [ ] W2.1.2 Implement/align `GetEffectivePolicies` / `GetEffectiveActions` to that surface.
- [ ] W2.1.3 Point OracleTool DescribeStage (and related) at the same API.
- [ ] W2.1.4 Fix Capability transition targets to real `Stage` refs via catalog (no empty stub stages).
- [ ] W2.1.5 Collapse or adapt BehaviorPass; delete third composition path.
- [ ] W2.1.6 Tests for effective policy counts / describe consistency on a multi-policy fixture.

## Primary files

- `Poly/DomainModeling/Analysis/CapabilityAnalyzer.cs`
- `Poly/DomainModeling/Analysis/BehaviorPass.cs`
- `Poly/DomainModeling/Analysis/DomainSemanticLookupExtensions.cs`
- `Poly.Mcp/Tools/OracleTool.cs`

## Acceptance criteria

- [ ] Single composition implementation.
- [ ] DescribeStage effective counts match helper.
- [ ] Build + tests green.

## Progress notes

(empty)
