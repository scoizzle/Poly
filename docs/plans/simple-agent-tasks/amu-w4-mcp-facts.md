# AMU-W4 — MCP structured facts from LatestAnalysis

**Wave:** 4  
**Difficulty:** M  
**Status:** `[ ]`  
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

- [ ] MCP smoke / DomainTools tests green  
- [ ] No DomainModeling semantic change required  
- [ ] Guide/tool text honest  

## File ownership

- **Edit:** Poly.Mcp tools + MCP tests; optional DomainQueries projection helpers  
- **Do not edit:** analyzers, lowering  

## Status

**Status:** Not Started  
