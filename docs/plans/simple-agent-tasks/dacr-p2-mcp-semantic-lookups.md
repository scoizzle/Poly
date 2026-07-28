# Micro-Task: DACR.P2 - MCP Semantic Surfaces Use Analysis Metadata

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Medium
Status: [ ] Not Started
Prereq: DACR.P1 complete

## Objective

Align MCP semantic responses with analysis truth and remove ad hoc semantic rediscovery.

## Tasks

- [ ] P2.1 Add or use shared semantic lookup helpers for relationship, action, and stage semantics.
- [ ] P2.2 Update OracleTool semantic describe routes to use metadata-backed lookups.
- [ ] P2.3 Keep structural projection routes direct where semantically neutral.
- [ ] P2.4 Enforce fail-closed behavior when session analysis is unavailable for semantic routes.

## Primary Files

- Poly.Mcp/Tools/OracleTool.cs
- Poly.Mcp/Tools/DomainTools.cs
- Poly/DomainModeling/Queries/DomainQueries.cs

## Acceptance Criteria

- [ ] MCP semantic describe outputs come from metadata-backed resolution.
- [ ] No semantic fallback scans in touched tool methods.
- [ ] Tools return explicit error when analysis is missing for semantic operations.

## Verification

- [ ] Build green.
- [ ] MCP tool smoke tests green.
- [ ] Regression tests for fail-closed missing-analysis behavior.
