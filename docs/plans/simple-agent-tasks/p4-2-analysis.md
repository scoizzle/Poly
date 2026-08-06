# P4-2 — Analysis quantifier vs cardinality

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** P4-1  

## Objective

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
