# DAS W4.2 — MCP, lowering, export: no analysis-present scans

**Wave:** W4 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) §5.2–5.5  
**Difficulty:** Medium  
**Status:** `[ ]`  
**Prereq:** W4.1  

## Objective

Oracle describe, effect lowering, and export helpers do not tree-scan when analysis is present. Residual scans only if analysis is null **and** that path is non-product or deleted.

## Tasks

- [ ] W4.2.1 Clear `DM-META-REMOVE-FALLBACK` in `OracleTool` describe routes (analysis-present already not-found—delete dead scan arms if safe).
- [ ] W4.2.2 EffectLoweringPass / DomainToCSharpExporter: analysis-present paths catalog-only; remove null-analysis product paths or isolate EntitySyntax-era comments.
- [ ] W4.2.3 DomainMutationContext: either catalog-only or single live-overlay mechanism documented (no ad-hoc multi-scan).
- [ ] W4.2.4 Tests for MCP not-found vs missing-metadata distinction where applicable.

## Acceptance criteria

- [ ] Grep markers in OracleTool + Lowering semantic files reduced per plan; analysis-present soft-scan gone.
- [ ] Build + tests green.

## Progress notes

(empty)
