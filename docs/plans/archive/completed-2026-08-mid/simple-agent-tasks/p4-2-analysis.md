# P4-2 — Analysis quantifier vs cardinality

**Difficulty:** S  
**Status:** `[x]` — DONE 2026-08-06

## Implementation notes

- Confirmed existing diagnostics in `SubscriptionAnalyzer`: undefined-quantifier check
  (line ~230) and quantifier-vs-cardinality error `isSingularFromSource`
  (OneToOne **and** ManyToOne) → `SubscriptionContractMismatch` (line ~277). No new
  product analysis code needed — the DSL could simply not author `when any|all` before
  p4-1, so these paths were IR-fixture-only.
- Added DSL-level fail-closed tests (`SubscriptionAnalysisTests`):
  - `Evolve_DslWhenAnyOnOneToOne_FailsClosed` — DSL `when any` on `one` nav → evolve **fails closed** with DMSS003 (review F5 promoted the previous warning; `ParseDomain` throws on errors).
  - `Analyze_DslWhenAllOnOneToMany_NoWarning` — DSL `when all` on `many` nav → no warning.
  - `Analyze_DslWhenAnyOnOneToMany_NoWarning` — DSL `when any` on `many` nav → no warning.
  - `Analyze_AnyQuantifierOnOneToOne_ReportsError` — IR fixture; warning → error (F5).
  - New `ParseDomain` helper (DSL → evolve → domain).
Ensure analysis validates Any/All vs relationship cardinality (singular + Any/All fail closed / existing diagnostic). No new dual path.

## Required reading

- SubscriptionAnalyzer / related diagnostic codes  
- Existing singular+Any/All warnings if any  

## Exact steps

1. Wire or confirm diagnostics for illegal quantifier+cardinality.  
2. Each on OneToMany remains OK.  
3. Tests for fail-closed cases.

## Verification

- [ ] Diagnostic tests green  

## File ownership

- **Edit:** SubscriptionAnalyzer (or owner of when analysis) + tests  
- **Do not edit:** parser (unless tiny), store  

## Status

**Status:** Not Started  
