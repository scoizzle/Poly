# e2e-p-2 — Mixed `require` / `require not` print

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** p-1  
**Fleet:** P4-2  

## Objective

`require A` plus `require not B` must not print `require A, not B` (unparseable).

## Exact steps

1. Failing round-trip: action/stage with both a positive and a negated require. Name: `Print_MixedRequireGates_RoundTrips`.
2. Printer emits a form `PolyDslParser` already accepts (split statements or repeated `require` — match existing parse, do not add grammar).

## Verification

- [ ] Printed text applies via the product parser  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DomainDslPrinter.cs` (require/gate emit only) | `PolyDslParser` grammar |
| matching tests | |

## Status

**Status:** Not Started  
**Claimed by:**  
