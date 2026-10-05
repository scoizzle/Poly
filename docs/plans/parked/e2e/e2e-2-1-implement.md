# e2e-2-1 — Implement the Q3′ export decision

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** 2-0  
**Repro:** `probes/fleet-eval/12-mcp/mcp-orders.poly` (`Pay` throws because `AllLinesShipped` is a guard)

## Objective

No domain silently ships un-callable actions because an entity-level `any`/`all`/`none`/`count` policy was prepended as a throwing guard.

## Exact steps

1. Failing test named for the lock: either `Export_AllLinesPolicy_ActionRuns` (A) or `Export_StoreOnlyQuantifierGuard_FailsClosed` (B).
2. Implement **only** what 2-0 locked. Do not do both.
3. Keep VM compiler throw if Q3′ nodes still must not reach it. Do not change store preprocess.
4. Touch: Q3′ lower/throw arms + action-guard prepend in `DomainToCSharpExporter` / `DomainExpressionLoweringPass`.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DomainExpressionLoweringPass.cs` (Q3′ arms only) | AddDays (e2e-r-8) |
| `DomainToCSharpExporter.cs` (policy methods + action guards) | Create unique, subscriptions, Minimal API |
| tests | |

## Status

**Status:** Not Started  
**Claimed by:**  
