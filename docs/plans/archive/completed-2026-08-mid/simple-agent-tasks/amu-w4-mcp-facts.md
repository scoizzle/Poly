# AMU-W4 — MCP structured facts from LatestAnalysis

**Wave:** 4  
**Difficulty:** M  
**Status:** `[x]` — DONE 2026-08-06
**Prereq:** W2 soft (bags stable)  

## Objective

Expand `get_domain_analysis` (or a thin sibling facts affordance) so agents see **paid-for** bags: at least ownership aggregate summary and subscription/capability signal — without a second fact store. Project `session.LatestAnalysis` only.

## Required reading

- `Poly.Mcp/Tools/DomainTools.cs` (`get_domain_analysis`)  
- `DomainQueries.GetAnalysisSummary`  
- velocity review P0.3 historical  

## Exact steps

1. Inventory current JSON fields on get_domain_analysis.  
2. Add structured summaries (examples): aggregate roots/count, stages with non-empty subscription plans, capability action names already partial — deepen honesty.  
3. No re-analysis; null analysis remains fail/empty per existing tool contract.  
4. MCP smoke test asserts new fields present when analysis has bags.  
5. Update tool description if claims change.

## Verification

- [x] MCP smoke / DomainTools tests green (McpSmokeTests incl. new GetDomainAnalysis tests)
- [x] No DomainModeling semantic change required (projection only in Poly.Mcp)
- [x] Guide/tool text honest (tool Description updated to claim aggregates + subscription plans)
- [x] Build green; full suite 1845/1845

## Implementation notes

`Poly.Mcp/Tools/DomainTools.cs` — `get_domain_analysis` projection only (no re-analysis; `LatestAnalysis` still the sole fact store):
- **New records:** `AggregateFact(rootName, memberNames)`, `SubscriptionPlanFact(entityName, stageName, relationshipName, targetStageNames)`.
- **`AnalysisData` extended:** `aggregateRootCount`, `aggregates`, `subscriptionPlans` (all defaulted/optional — serialization backward compatible).
- **Aggregates** from `OwnershipAggregateMetadata` (OwnershipAggregatePass): per root, transitive member names via `AggregateParentName` chain (walk, not recursion).
- **Subscription plans** from `SubscriptionDispatchPlanMetadata` bags (RuntimeContractAnalyzer): entity-level (always-active) plans on the entity node → `stageName: null`; stage-scoped plans on the stage node → `stageName: <name>`; target stages = distinct `StageNames` sorted.
- Tool Description updated: "(entity structure, aggregates, topology, actions, subscription plans)".

**Tests:**
- Extended `GetDomainAnalysis_WithEntityAndRelationship_IncludesStructuredFacts` → asserts `AggregateRootCount >= 1` + `Aggregates` non-null.
- New `GetDomainAnalysis_WithStageSubscriptions_IncludesSubscriptionPlans` — apply_dsl domain with `when loans Overdue` (stage plan) + entity-level `when`; asserts stage plan fact (Patron/loans→Overdue) AND entity-level plan with null stage name AND aggregate facts present.

**Note:** plan bags are published even for zero-subscription stages (empty dict) — projection emits empty `subscriptionPlans` list (non-null) in that case.

- **Edit:** Poly.Mcp tools + MCP tests; optional DomainQueries projection helpers  
- **Do not edit:** analyzers, lowering  

## Status

**Status:** Done — 2026-08-06 (see Implementation notes)  
