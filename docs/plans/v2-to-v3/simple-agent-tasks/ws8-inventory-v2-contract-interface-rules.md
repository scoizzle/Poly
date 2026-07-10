# Micro-Task: Inventory V2 contract interface rules for V3 port

**Parent Workstream**: WS8  
**Difficulty**: Small Model Friendly  
**Estimated Tokens**: ~5k  
**Status**: [ ] Not Started

## Objective

Produce a short, accurate inventory of V2 contract interface generation rules so a follow-up task can implement V3 parity without rediscovering AGENTS.md folklore.

## Context You Need

- AGENTS.md section **Contract Interface Generation** (naming, inheritance, action placement)
- V2 source: search `LowerToContractInterfaces` / `DomainImplementationLowering` under `Poly/Data/Modeling`
- `docs/decisions/2026-06-08-domain-lowering-boundary.md`

## Exact Steps

1. Locate the V2 method(s) that emit contract interfaces.
2. Extract rules into a markdown table in your summary (or a small file under `docs/plans/v2-to-v3/spikes/` if substantial):
   - Interface naming
   - Base / parent stage inheritance
   - Which actions appear on which interfaces
   - Abstract vs concrete stages
3. Note file paths + line ranges (approximate).
4. List **gaps** vs V3 today (what does not exist under `Poly/DomainModeling/Lowering`).
5. Propose the **smallest** first V3 deliverable (e.g. one entity + one stage → one interface type definition AST).

## Verification

- [ ] Inventory cites real code paths
- [ ] Does not implement full codegen in this task
- [ ] Fits in agent-summary or one spike file ≤ ~150 lines

## Output

- Agent summary: `agent-summaries/ws8-contract-interface-inventory-YYYY-MM-DD.md`
- Optional: `docs/plans/v2-to-v3/spikes/v3-contract-interface-rules.md`

## Out of Scope

- Implementing generators
- Changing AGENTS.md rules (document only)
