# Micro-Task: DACR.P2 - MCP Semantic Surfaces Use Analysis Metadata

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Medium
Status: [~] Reopened — describe policy coverage + analysis-present fail-closed (F3–F4)
Prereq: DACR.P1 complete
Active follow-ups: ./dacr-followups-2026-07-30.md (F3, F4)

## Objective

Align MCP semantic responses with analysis truth and remove ad hoc semantic rediscovery.

## Tasks

- [ ] P2.1 Add or use shared semantic lookup helpers for relationship, action, and stage semantics.
- [ ] P2.2 Update OracleTool semantic describe routes to use metadata-backed lookups.
- [ ] P2.3 Keep structural projection routes direct where semantically neutral.
- [x] P2.4 Enforce fail-closed behavior when session analysis is unavailable for semantic routes.

## Primary Files

- Poly.Mcp/Tools/OracleTool.cs
- Poly.Mcp/Tools/DomainTools.cs
- Poly/DomainModeling/Queries/DomainQueries.cs

## Acceptance Criteria

- [x] MCP semantic describe outputs come from metadata-backed resolution. DescribePolicy now searches entity, stage, and action MTI maps (F3).
- [x] No semantic fallback scans in touched tool methods when analysis present (F4 — fail closed).
- [x] Tools return explicit error when analysis is missing for semantic operations (effect routes).

## Verification

- [x] Build green.
- [x] MCP tool smoke tests green.
- [x] Regression tests for fail-closed missing-analysis behavior.

## Progress Notes (2026-07-30 r2 — all follow-ups closed)

- [x] Oracle semantic effect routes (`analyze_effect`, `lower_effect_to_csharp`) now require `state.LatestAnalysis` and fail closed with explicit guidance when missing.
- [x] Lowering context in these routes now threads `Analysis` and `Domain` so semantic lowering uses analysis-first paths.
- [x] Migrated OracleTool semantic describe routes to metadata-backed lookups.
- [x] All structural projection routes (entity list, snapshot) kept direct.
- [x] F3 resolved: DescribePolicy searches entity, stage, action MTI maps with scope disambiguation.
- [x] F4 resolved: all four describe routes fail closed when analysis present — no soft-scan fallback.
- Validation evidence (last full run):
  - dotnet build (0 errors)
  - dotnet run --project Poly.Tests/Poly.Tests.csproj (1703 passed, 0 failed)
