# DAS W2.1 — Unify effective action/policy surface

**Wave:** W2 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) §3.3, W2  
**Difficulty:** Medium  
**Status:** `[x]`  
**Prereq:** W1 gate  

## Objective

One algorithm answers “effective policies/actions at stage.” MCP, capability views, and helpers agree. BehaviorPass becomes a thin pack DTO adapter or is removed.

## Tasks

- [x] W2.1.1 Choose canonical surface (Capability recommended) and document composition rules (entity + stage; not all action policies unless product says so).
- [x] W2.1.2 Implement/align `GetEffectivePolicies` / `GetEffectiveActions` to that surface. *(unknown stage fail-closed — symmetric)*
- [x] W2.1.3 Point OracleTool DescribeStage (and related) at the same API.
- [x] W2.1.4 Fix Capability transition targets to real `Stage` refs via catalog (no empty stub stages).
- [x] W2.1.5 Collapse or adapt BehaviorPass; delete third composition path.
- [x] W2.1.6 Tests for effective policy counts / describe consistency on a multi-policy fixture.

## Primary files

- `Poly/DomainModeling/Analysis/CapabilityAnalyzer.cs`
- `Poly/DomainModeling/Analysis/BehaviorPass.cs`
- `Poly/DomainModeling/Analysis/DomainSemanticLookupExtensions.cs`
- `Poly.Mcp/Tools/OracleTool.cs`

## Acceptance criteria

- [x] Single composition implementation.
- [x] DescribeStage effective counts match helper.
- [x] Build + tests green.
- [x] Unknown `stageName` fail-closed on both helpers (no entity-policy vacuous success).

## Progress notes

- Implement landed (unify path):
  - Canonical surface: `StageCapabilityMetadata` / `StageCapabilityView` via `CapabilityAnalyzer`.
  - Composition algorithm: `DomainEffectiveSurface` — policies = entity + stage (no action policies); actions = stage-local only.
  - `GetEffectivePolicies` / `GetEffectiveActions` prefer StageCapability; catalog Index / stage-local fallback reuses the same compose helpers.
  - `OracleTool.DescribeStage` uses helpers only (analysis-present path).
  - Transition targets: catalog `StagesByEntity` real refs; no empty stub stages.
  - `BehaviorPass`: pack DTO adapter (Capability transitions + Semantic action EPM); offline `BuildBehavior` runs pipeline (no effect-walk dual path).
  - Semantic stage EPM uses `DomainEffectiveSurface` (same algorithm).
  - Tests: multi-policy fixture, describe/helper consistency, real transition stage refs.
  - CORE §3.1 note for DAS W2 effective surface.
- **Verify fix (unknown-stage fail-closed):**
  - `GetEffectivePolicies` now requires `TryGetStage` success before StageCapability or catalog compose (symmetric with `GetEffectiveActions`).
  - Tests: `GetEffectivePolicies_UnknownStage_ReturnsEmpty_NotEntityPolicies` (cap + catalog paths); `GetEffectiveActions_UnknownStage_ReturnsEmpty`.
  - Build + effective-surface tests green.
- **Verify pass (severity none):** Source review —
  `DomainSemanticLookupExtensions` (TryGetStage fail-closed on both helpers),
  `DomainEffectiveSurface` (single compose),
  `CapabilityAnalyzer` (StageCapability via compose; transition targets from catalog `StagesByEntity` only),
  `BehaviorPass` (DTO adapter; `BuildBehavior` → `DomainModelAnalyzer`),
  `SemanticDomainAnalyzer.PublishEffectivePolicies` (`ComposeStagePolicies`),
  `OracleTool.DescribeStage` (helpers when `LatestAnalysis` set).
  Tests: unknown-stage empty (cap+catalog strip), multi-policy exclude action policies,
  ActionCapability real Stage refs, `DescribeStage_EffectiveCounts_MatchHelpers`.
  CORE §3.1 W2 note present. Gate W2 evidence filled.
  No residual unknown-stage entity-policy vacuous path in production helpers.
  (Read-only verifier did not re-run git/dotnet.)
