# e2e-x-5 — User policy `not_X` is not synthetic negation

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** x-4  
**Fleet:** P3-14  

## Objective

Exporter must not strip `not_` and gate the wrong policy. Store a negated flag; do not mangle names.

## Exact steps

1. Test: export + runtime agree on `require not_Paid` when a user policy is named `not_Paid`.  
2. Replace name-mangling with an explicit negation flag on the require/gate IR or emit site.

## File ownership

| Edit | Do not edit |
|------|-------------|
| exporter gate emit + any require model that stores the flag | parser grammar unless flag already exists |
| tests | |

## Status

**Status:** Not Started  
**Claimed by:**  
