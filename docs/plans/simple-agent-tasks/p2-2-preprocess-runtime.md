# P2-2 — EvaluatePolicy multi-hop preprocess

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** P2-0; soft after P2-1  

## Objective

`PreprocessQuantifiers` / path-prefix preprocess walks nested navs: resolve outbound links hop-by-hop (singular fail-closed on 0 or >1), evaluate leaf on final bag.

## Required reading

- DomainEntityInstance preprocess / RelationshipNavigation handling  
- Absorption algorithm sketch  

## Exact steps

1. Recurse preprocess for nested nav.  
2. Fail closed multi-link at hop; empty links → false/exists semantics consistent with single-hop.  
3. Unit tests on preprocess if exposed; else via EvaluatePolicy.

## Verification

- [ ] Multi-hop eval tests green (may share P2-3)  

## File ownership

- Runtime preprocess path + tests  
- **Do not edit:** guide  

## Status

**Status:** Not Started  
