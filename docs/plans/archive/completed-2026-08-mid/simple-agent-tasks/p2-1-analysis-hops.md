# P2-1 — Analysis for multi-hop path-prefix

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** P2-0  

## Objective

Policy/expression analysis validates each hop: relationship exists, source entity matches, **to-one** for bare path-prefix chains; property leaf exists on final entity. Fail closed on many-middle without quantifier.

## Required reading

- PolicyConstraintAnalyzer nav handling  
- RelationshipNavigation shape  

## Exact steps

1. Extend validation for nested RelationshipNavigation.  
2. Diagnostics for unknown hop / many cardinality on bare chain.  
3. Tests for reject + happy two-hop.

## Verification

- [ ] Analysis tests green  

## File ownership

- PolicyConstraintAnalyzer (+ related) + tests  
- **Do not edit:** preprocess runtime until P2-2 if split cleanly  

## Status

**Status:** Not Started  
