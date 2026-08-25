# DAS W4.2 — MCP, lowering, export: no analysis-present scans

**Wave:** W4 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) §5.2–5.5  
**Difficulty:** Medium  
**Status:** `[x]`  
**Prereq:** W4.1  

## Objective

Oracle describe, effect lowering, and export helpers do not tree-scan when analysis is present. Residual scans only if analysis is null **and** that path is non-product or deleted.

## Tasks

- [x] W4.2.1 Clear `DM-META-REMOVE-FALLBACK` in `OracleTool` describe routes (analysis-present already not-found—delete dead scan arms if safe).
- [x] W4.2.2 EffectLoweringPass / DomainToCSharpExporter: analysis-present paths catalog-only; remove null-analysis product paths or isolate EntitySyntax-era comments.
- [x] W4.2.3 DomainMutationContext: either catalog-only or single live-overlay mechanism documented (no ad-hoc multi-scan).
- [x] W4.2.4 Tests for MCP not-found vs missing-metadata distinction where applicable.

## Acceptance criteria

- [x] Grep markers in OracleTool + Lowering semantic files reduced per plan; analysis-present soft-scan gone.
- [x] Build + tests green.

## Progress notes

### 2026-07-31 — implement (pass)

**Implement success:** true · **Build:** 0 errors · **Tests:** 1760 passed, 0 failed

- **OracleTool describe (stage/action/policy/relationship):** deleted null-analysis structural scan arms; require `LatestAnalysis`; catalog/ESM-backed only. Distinguish **missing metadata** (catalog/ESM stripped) vs **not found** (catalog complete, name absent). Zero `DM-META-REMOVE-FALLBACK` in `OracleTool.cs`.
- **EffectLoweringPass:** analysis-present stage resolve is `TryGetStage` only; null-analysis structural stage list kept as non-product residual (no marker). Removed DefaultForDomainType fallback marker.
- **DomainToCSharpExporter:** CreateNav target via `GetTypeLookup` (throw if missing); constructor order requires ESM (throw if missing); `BuildEnumPropertyNames` catalog-only when analysis present; `TryResolveEnumType` / `ResolveRelationship` analysis-present fail closed; null-analysis residual untagged. Zero markers in Lowering semantic files.
- **DomainMutationContext:** documented catalog-first + single live-overlay for in-batch adds; collapsed dual `DM-META-REMOVE-FALLBACK` arms into one overlay path; zero markers.
- **Tests:** `DomainSemanticLookupFailClosedTests` — missing-metadata vs not-found pairs for stage/action/policy/relationship.
- Residual markers remain outside W4.2 scope (e.g. DslCompiler) → **W4.3**.

### 2026-07-31 — verify (pass, severity suggestion)

**Verify pass:** true · **Severity:** suggestion · Live suite not re-run (static re-check of W4.2 AC).

- **rg `DM-META-REMOVE-FALLBACK`:** 0 hits in `OracleTool.cs`, `Poly/DomainModeling/Lowering/`, `Poly/DomainModeling/Evolution/` (markers remain only under `src/Poly.DslCompiler` and docs → **W4.3**).
- **OracleTool** `DescribeStage` / `Action` / `Policy` / `Relationship` require `LatestAnalysis`; catalog/ESM-backed resolve; missing-metadata messages distinct from not-found (`OracleTool.cs` ~530–688).
- **EffectLoweringPass** `StageTransition` uses `TryGetStage` only when analysis present; null-analysis `entity.Stages` residual retained; `ResolveEntity` / `ResolveRelationship` analysis-present return null without domain rescan (~123–147, ~482–518). Create-in requires non-null analysis (~315–318).
- **DomainToCSharpExporter:** `CreateNav` `GetTypeLookup` throw (~633–635); `GetConstructorParameters` ESM throw (~731–736); `BuildEnumPropertyNames` / `TryResolveEnumType` / `ResolveRelationship` analysis-present fail closed (~1202–1281).
- **DomainMutationContext** type remarks document catalog-first + single live-overlay; `ResolveStage` / `ResolveAction` implement that (~11–17, ~233–318).
- **Tests:** `DomainSemanticLookupFailClosedTests` `Describe*` missing-metadata vs not-found pairs for stage/action/policy/relationship (~534–715) plus `ResolveRelationship` RLM throw/not-found.
- **Suggestions (not AC blockers):** residual monopath `domain.Types` walks and MCP analyze/lower structural finds. W4.2 dual-path soft-scan AC met; `das-gate` **G4.2** left open until wave/gate ceremony covers residual monopath + W4.3 markers.
