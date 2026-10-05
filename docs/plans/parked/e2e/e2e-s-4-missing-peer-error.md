# e2e-s-4 — Missing subscriber/peer property is an error

**Difficulty:** S  
**Status:** `[ ]`  
**Fleet:** P6-4  
**Parallel with:** s-1  

## Objective

Missing subscriber/peer property is an **analysis error**, not a warning that DslCompiler drops → late CS1061.

## Exact steps

1. Failing test: `missing-subscriber-prop` (fleet) rejected at analysis (`Severity.Error`). Name: `Subscription_MissingPeerProperty_AnalysisError`.
2. Escalate in `SubscriptionAnalyzer` only.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Analysis/SubscriptionAnalyzer.cs` | exporter |
| tests | store |

## Status

**Status:** Not Started  
**Claimed by:**  
