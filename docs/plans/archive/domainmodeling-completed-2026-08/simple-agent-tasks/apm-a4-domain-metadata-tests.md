# Micro-Task: APM.A4 — Domain analysis exposes topology/aggregate/behavior

**Suite:** [`apm-README.md`](apm-README.md) **#A4**  
**Parent:** [`../analysis-pipeline-merge.md`](../analysis-pipeline-merge.md) §5 Phase A4, §8  
**Difficulty:** Medium  
**Estimated Context:** ~12k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** **A2** (A3 recommended)

## Objective

Prove `DomainModelAnalyzer.Analyze(domain)` (no DslCompiler) attaches:

- `EffectTopologyMetadata`
- `OwnershipAggregateMetadata`
- `BehaviorMetadata`

on the domain node for a minimal multi-entity fixture.

## Required Reading

- Parent §5 A4 / §8  
- Existing domain analysis tests under `Poly.Tests/DomainModeling/Analysis/` (style)  
- Metadata types: `EffectTopologyMetadata`, `OwnershipAggregateMetadata`, `BehaviorMetadata`  
- How tests build domains (parser + evolution or builders)

## Exact Steps

1. Minimal domain: e.g. Customer + Order, `orders: many Order`, one action/policy if needed for Behavior.
2. `var result = DomainModelAnalyzer.Analyze(domain);`
3. Assert three metadata bags non-null on `domain`.
4. Optional light asserts: aggregate marks Customer root / Order non-root; behavior lists an action name.
5. Place under `Poly.Tests/DomainModeling/Analysis/` with TUnit naming `Method_Condition_ExpectedResult`.
6. No new diagnostic code asserts (Phase B).

## Verification

- [ ] New tests green  
- [ ] Does not require DslCompiler  
- [ ] Suite subset for Analysis green  

## Output

- Test file(s)  
- Status Done  

## Out of Scope

- MCP smoke  
- Codegen  
- DMAGG/DMEFF codes  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
