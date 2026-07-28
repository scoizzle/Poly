# Micro-Task: DACR.P2 - MCP Semantic Surfaces Use Analysis Metadata

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Medium
Status: [~] In Progress
Prereq: DACR.P1 complete

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

- [ ] MCP semantic describe outputs come from metadata-backed resolution.
- [ ] No semantic fallback scans in touched tool methods.
- [ ] Tools return explicit error when analysis is missing for semantic operations.

## Verification

- [x] Build green.
- [x] MCP tool smoke tests green.
- [x] Regression tests for fail-closed missing-analysis behavior.

## Progress Notes

- [x] Oracle semantic effect routes (`analyze_effect`, `lower_effect_to_csharp`) now require `state.LatestAnalysis` and fail closed with explicit guidance when missing.
- [x] Lowering context in these routes now threads `Analysis` and `Domain` so semantic lowering uses analysis-first paths.
- [ ] Remaining P2 work: migrate semantic lookup internals to shared metadata-backed helpers and remove residual ad hoc scans from targeted describe routes.
